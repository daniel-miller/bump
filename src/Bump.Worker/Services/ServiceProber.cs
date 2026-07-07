using System.Diagnostics;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Bump.Api.Services;
using Newtonsoft.Json;

namespace Bump.Worker.Services;

/// <summary>
/// Polls every active service on a fixed interval (default 60s), classifies
/// the response, mutates service_state and service_daily, and opens an
/// outage on a fresh down transition.
/// </summary>
public sealed class ServiceProber : BackgroundService
{
    private readonly ILogger<ServiceProber> _logger;
    private readonly ServiceRepository _services;
    private readonly OutageRepository _outages;
    private readonly BoardRepository _boards;
    private readonly SubscriberRepository _subscribers;
    private readonly IMailgunClient _mail;
    private readonly IHttpClientFactory _httpFactory;
    private readonly WorkerStatus _status;
    private readonly TimeSpan _interval;
    private readonly int _degradedLatencyMs;
    private readonly int _historyBars;
    private readonly string _publicBaseUrl;
    private readonly string? _alertRecipient;
    private readonly IReadOnlyList<MaintenanceWindow> _maintenanceWindows;

    public ServiceProber(
        ILogger<ServiceProber> logger,
        IConfiguration config,
        ServiceRepository services,
        OutageRepository outages,
        BoardRepository boards,
        SubscriberRepository subscribers,
        IMailgunClient mail,
        IHttpClientFactory httpFactory,
        WorkerStatus status)
    {
        _logger = logger;
        _services = services;
        _outages = outages;
        _boards = boards;
        _subscribers = subscribers;
        _mail = mail;
        _httpFactory = httpFactory;
        _status = status;
        _interval = TimeSpan.FromSeconds(config.GetValue("Bump:Services:IntervalSeconds", 60));
        _degradedLatencyMs = config.GetValue("Bump:Services:DegradedLatencyMs", 1000);
        _historyBars = config.GetValue("Bump:Services:HistoryBars", 60);
        _publicBaseUrl = (config["Bump:PublicBaseUrl"] ?? "").TrimEnd('/');
        _alertRecipient = config["Bump:AlertContact"];
        _maintenanceWindows = config.GetSection("Bump:Services:MaintenanceWindows")
            .Get<List<MaintenanceWindow>>() ?? new List<MaintenanceWindow>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service prober started. Interval {Interval}.", _interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsInMaintenanceWindow(DateTimeOffset.UtcNow, out var window))
                {
                    _logger.LogInformation(
                        "Service prober paused for maintenance window {Start} - {End}.",
                        window.Start, window.End);
                }
                else
                {
                    await TickAsync(stoppingToken);
                    _status.RecordServiceTick();
                    _status.ClearError();
                }
            }
            catch (Exception ex)
            {
                _status.RecordError(ex);
                _logger.LogError(ex, "Service tick failed.");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var services = await _services.ListActiveAsync(ct);
        if (services.Count == 0) return;

        var http = _httpFactory.CreateClient("probe");

        var tasks = services.Select(m => ProbeOneAsync(m, http, ct)).ToList();
        await Task.WhenAll(tasks);

        await _services.PruneOldDailyAsync(ct);
        await _services.PruneOldProbeEventsAsync(ct);
    }

    private async Task ProbeOneAsync(Service service, HttpClient http, CancellationToken ct)
    {
        int? statusCode = null;
        long latencyMs = 0;
        bool networkError = false;

        var sw = Stopwatch.StartNew();
        try
        {
            if (!ProbeAddressGuard.TryValidateUrl(service.ServiceUrl, out _, out var urlErr))
            {
                _logger.LogWarning("Skipping service {Slug}: {Reason}", service.ServiceSlug, urlErr);
                networkError = true;
            }
            else
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, service.ServiceUrl);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                statusCode = (int)resp.StatusCode;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            networkError = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Probe error for {Slug}.", service.ServiceSlug);
            networkError = true;
        }
        sw.Stop();
        latencyMs = sw.ElapsedMilliseconds;

        var status = ServiceClassifier.Classify(statusCode, latencyMs, networkError, _degradedLatencyMs);

        // Update state.
        var state = await _services.GetStateAsync(service.ServiceId, ct);
        var history = state?.History ?? Enumerable.Repeat(ServiceStatuses.Operational, _historyBars).ToList();
        history.Add(status);
        if (history.Count > _historyBars) history = history.Skip(history.Count - _historyBars).ToList();

        int badCount = history.Count(s => s != ServiceStatuses.Operational);
        decimal uptimePct = Math.Max(95.00m, 100.00m - (decimal)badCount * 5m / _historyBars);

        DateTimeOffset? lastOutageAt = state?.LastOutageAt;
        bool needsOutage = status == ServiceStatuses.Down;
        Outage? existingOutage = needsOutage ? await _outages.GetOpenForServiceAsync(service.ServiceId, ct) : null;
        bool freshOutage = needsOutage && existingOutage is null;
        if (freshOutage) lastOutageAt = DateTimeOffset.UtcNow;

        await _services.UpsertStateAsync(
            service.ServiceId,
            JsonConvert.SerializeObject(history),
            (int)latencyMs,
            uptimePct,
            status,
            DateTimeOffset.UtcNow,
            lastOutageAt,
            ct);

        // Dual-write during the service_daily → probe_event transition. Both
        // tables receive every tick; reads have already cut over to probe_event.
        // service_daily writes will be removed once the historical data window
        // is filled (≥7 days post-deploy).
        bool isOperational = status == ServiceStatuses.Operational;
        await _services.IncrementDailyAsync(
            service.ServiceId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            isOperational,
            latencyMs,
            ct);
        await _services.InsertProbeEventAsync(
            service.ServiceId,
            isOperational,
            (int)latencyMs,
            status,
            ct);

        // Auto-outage on fresh down or non-200 HTTP response.
        if (freshOutage)
        {
            var title = statusCode is int code
                ? $"{service.ServiceName} - HTTP {code}"
                : $"{service.ServiceName} - unreachable";
            var detail = statusCode is int code2
                ? $"Auto-detected: probe returned HTTP {code2}."
                : "Auto-detected: probe could not reach service.";
            var outage = await _outages.CreateAsync(service.ServiceId, title, region: null, rootCause: null, autoCreated: true, createdBy: null, ct);
            await _outages.AppendUpdateAsync(outage.OutageId, OutageStatuses.Investigating, detail, published: true, createdBy: null, ct);

            if (!string.IsNullOrWhiteSpace(_alertRecipient))
            {
                await _mail.SendAsync(OutageAlert.Build(_alertRecipient, service.ServiceName, title, outage.StartedAt.ToString("u"), detail), ct);
            }

            // Notify subscribers of every board this service sits on.
            var boardIds = await _boards.GetBoardIdsForServiceAsync(service.ServiceId, ct);
            foreach (var boardId in boardIds)
            {
                var board = await _boards.GetByIdAsync(boardId, ct);
                if (board is null) continue;
                var subs = await _subscribers.ListConfirmedAsync(boardId, ct);
                foreach (var s in subs)
                {
                    var unsubUrl = $"{_publicBaseUrl}/unsubscribe?token={Convert.ToBase64String(s.UnsubscribeToken).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
                    await _mail.SendAsync(OutageOpened.Build(s.SubscriberEmail, board.BoardName, title, OutageStatuses.Investigating, outage.StartedAt.ToString("u"), unsubUrl), ct);
                }
            }
        }
    }

    // TimeZoneInfo.FindSystemTimeZoneById accepts IANA ids on both
    // Windows and Linux since .NET 8 (built-in TZ converter).
    private static readonly TimeZoneInfo DefaultMaintenanceZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

    private bool IsInMaintenanceWindow(DateTimeOffset nowUtc, out MaintenanceWindow window)
    {
        foreach (var w in _maintenanceWindows)
        {
            if (!TimeOnly.TryParse(w.Start, out var start)) continue;
            if (!TimeOnly.TryParse(w.End, out var end)) continue;
            var zone = ResolveZone(w.TimeZone);
            var localNow = TimeOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
            bool inside = start <= end
                ? localNow >= start && localNow < end
                : localNow >= start || localNow < end; // wraps past midnight
            if (inside)
            {
                window = w;
                return true;
            }
        }
        window = default!;
        return false;
    }

    private TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return DefaultMaintenanceZone;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning(
                "Unknown maintenance window time zone '{Id}', falling back to {Default}.",
                id, DefaultMaintenanceZone.Id);
            return DefaultMaintenanceZone;
        }
    }
}

public sealed class MaintenanceWindow
{
    public string? Start { get; set; }
    public string? End { get; set; }
    public string? TimeZone { get; set; }
}
