using Bump.Api;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Npgsql;

namespace Bump.Worker;

public class AlertWorker : BackgroundService
{
    private readonly ILogger<AlertWorker> _logger;
    private readonly string _connectionString;
    private readonly IMailgunClient _mail;
    private readonly string _alertRecipient;
    private readonly string _publicBaseUrl;
    private readonly WorkerStatus _status;
    private readonly TimeSpan _interval;

    public AlertWorker(ILogger<AlertWorker> logger, IConfiguration config, AlertsSettings alerts, IMailgunClient mail, WorkerStatus status)
    {
        _logger = logger;
        _status = status;
        _mail = mail;
        _connectionString = config["Bump:Database:ConnectionString"] ?? "";
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "Bump:Database:ConnectionString is empty. Set it via config/appsettings.work.json or the Bump__Database__ConnectionString environment variable.");
        }
        _alertRecipient = alerts.Contact;
        _publicBaseUrl = (config["Bump:Web:BaseUrl"] ?? "").TrimEnd('/');
        _interval = alerts.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alert worker started. Polling every {Interval}.", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
                _status.RecordPoll();
                _status.ClearError();
            }
            catch (Exception ex)
            {
                _status.RecordError(ex);
                _logger.LogError(ex, "Alert poll failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT p.problem_fingerprint,
                   e.environment_slug,
                   e.environment_name,
                   a.app_slug,
                   a.app_name,
                   p.problem_type,
                   p.problem_title,
                   COUNT(*) AS occurrences,
                   MAX(p.reported_at) AS last_seen,
                   MAX(p.problem_key) AS latest_key
            FROM problem p
            JOIN app         a ON a.app_key         = p.app_key
            JOIN environment e ON e.environment_key = p.environment_key
            WHERE p.dispatched_at IS NULL
            GROUP BY p.problem_fingerprint, e.environment_slug, e.environment_name, a.app_slug, a.app_name, p.problem_type, p.problem_title
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var alerts = new List<ProblemDigest.DigestEntry>();
        while (await reader.ReadAsync(ct))
        {
            alerts.Add(new ProblemDigest.DigestEntry(
                Fingerprint:       reader.GetString(0),
                Environment:       reader.GetString(1),
                EnvironmentName:   reader.GetString(2),
                AppSlug:           reader.GetString(3),
                AppName:           reader.GetString(4),
                Type:              reader.GetString(5),
                Title:             reader.GetString(6),
                Occurrences:       (int)reader.GetInt64(7),
                LastSeen:          new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)),
                LatestProblemKey:  reader.GetInt64(9)));
        }
        await reader.CloseAsync();

        foreach (var alert in alerts)
        {
            await _mail.SendAsync(ProblemDigest.Build(_alertRecipient, alert, _publicBaseUrl), ct);
            await MarkDispatchedAsync(conn, alert.Fingerprint, ct);
            _logger.LogInformation("Alerted on {Fingerprint} ({Type} in {App}/{Env})",
                alert.Fingerprint, alert.Type, alert.AppSlug, alert.Environment);
        }
    }

    private static async Task MarkDispatchedAsync(NpgsqlConnection conn, string fingerprint, CancellationToken ct)
    {
        const string sql = """
            UPDATE problem
            SET dispatched_at = now()
            WHERE problem_fingerprint = @fingerprint AND dispatched_at IS NULL
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("fingerprint", fingerprint);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
