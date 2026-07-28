using System.Data;
using System.Net;
using System.Net.Sockets;
using Bump.Api;
using Bump.Api.Mail;
using Bump.Api.Services;
using Bump.Worker;
using Bump.Worker.Announcements;
using Bump.Worker.Idempotency;
using Bump.Worker.Services;
using Bump.Worker.Subscribers;
using Dapper;
using Npgsql;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// Dapper 2.1.66 has no built-in binder for DateOnly on Npgsql parameters.
// Without this, IncrementDailyAsync throws every prober tick and no rows
// land in service_daily. Map to DbType.Date so Npgsql sends PG `date`
// regardless of DateTime.Kind (avoids timestamptz session-TZ skew).
SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

try
{
    // ContentRoot pinned to AppContext.BaseDirectory so appsettings.json and
    // appsettings.work.json (linked into the bin/ output via csproj <None>)
    // load regardless of the caller's working directory.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Configuration.AddJsonFile("appsettings.work.json", optional: true, reloadOnChange: true);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Bump.Worker";
    });

    // Levels come from the Serilog section, shared with Bump.Api. Only the file path is
    // per-subsystem, and it has its own key so production can write outside the app
    // directory without a redeploy.
    var workerLogPath = builder.Configuration["Bump:Worker:LogPath"];
    if (string.IsNullOrWhiteSpace(workerLogPath))
    {
        workerLogPath = Path.Combine("tmp", "logs", "worker");
    }
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .ReadFrom.Services(services)
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(Path.Combine(workerLogPath, "serilog-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    // Own listen address. Bump.Api links the same appsettings.json, so a shared root Urls
    // key put both hosts on 8080 and the second one to start died on bind.
    var workerUrls = builder.Configuration["Bump:Worker:Hosting:Urls"];
    if (string.IsNullOrWhiteSpace(workerUrls))
    {
        throw new InvalidOperationException(
            "Bump:Worker:Hosting:Urls is empty. It is the address the health endpoint binds. Set it via "
            + "config/appsettings.work.json or the Bump__Worker__Hosting__Urls environment variable.");
    }
    builder.WebHost.UseUrls(workerUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // Same reasoning as Bump.Api: outage-alert and digest emails embed this link.
    var webBaseUrl = builder.Configuration["Bump:Web:BaseUrl"];
    if (string.IsNullOrWhiteSpace(webBaseUrl))
    {
        throw new InvalidOperationException(
            "Bump:Web:BaseUrl is empty. It is the public status-page URL embedded in outage alerts and "
            + "problem digests. Set it via config/appsettings.work.json or the Bump__Web__BaseUrl "
            + "environment variable.");
    }

    var connectionString = builder.Configuration["Bump:Database:ConnectionString"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Bump:Database:ConnectionString is empty. Set it via config/appsettings.work.json or the Bump__Database__ConnectionString environment variable.");
    }
    var dataSource = NpgsqlDataSource.Create(connectionString);
    builder.Services.AddSingleton(dataSource);

    // ---- Bound sections ----
    // Defaults live on the settings classes in Bump.Api/BumpSettings.cs, shared with the API
    // so Bump:Services means the same thing in both hosts.
    var serviceSettings = builder.Configuration.GetSection("Bump:Services").Get<ServicesSettings>() ?? new ServicesSettings();
    var alertSettings = builder.Configuration.GetSection("Bump:Worker:Alerts").Get<AlertsSettings>() ?? new AlertsSettings();
    var announcementSettings = builder.Configuration.GetSection("Bump:Worker:Announcements").Get<AnnouncementsSettings>() ?? new AnnouncementsSettings();

    // AlertWorker used to throw on a missing recipient from its constructor, which surfaces as
    // a hosted-service start failure rather than a config error. Check it here instead.
    if (string.IsNullOrWhiteSpace(alertSettings.Contact))
    {
        throw new InvalidOperationException(
            "Bump:Worker:Alerts:Contact is empty. It is the recipient of alert digests and outage "
            + "emails. Set it via config/appsettings.work.json or the Bump__Worker__Alerts__Contact "
            + "environment variable.");
    }

    builder.Services.AddSingleton(serviceSettings);
    builder.Services.AddSingleton(alertSettings);
    builder.Services.AddSingleton(announcementSettings);

    builder.Services.AddSingleton<ServiceRepository>();
    builder.Services.AddSingleton<OutageRepository>();
    builder.Services.AddSingleton<BoardRepository>();
    builder.Services.AddSingleton<AnnouncementRepository>();
    builder.Services.AddSingleton<SubscriberRepository>();

    var mailgun = new MailgunOptions
    {
        ApiKey = builder.Configuration["Bump:Mailgun:ApiKey"] ?? "",
        Domain = builder.Configuration["Bump:Mailgun:Domain"] ?? "",
        From = builder.Configuration["Bump:Mailgun:From"] ?? "Bump <bump@example.com>",
        Region = builder.Configuration["Bump:Mailgun:Region"] ?? "us"
    };
    // Optional, matching Bump.Api and the README. MailgunClient logs and returns on every
    // send when unconfigured, so the worker still probes and schedules; it just cannot mail
    // an outage alert or a digest.
    if (string.IsNullOrWhiteSpace(mailgun.ApiKey) || string.IsNullOrWhiteSpace(mailgun.Domain))
    {
        Log.Warning("Bump:Mailgun:ApiKey or Bump:Mailgun:Domain is empty. Outbound email is disabled - "
            + "outage alerts and problem digests will not be delivered. Set both in "
            + "config/appsettings.work.json to enable them.");
    }
    builder.Services.AddSingleton(mailgun);
    builder.Services.AddHttpClient<IMailgunClient, MailgunClient>();

    // Probe HTTP client (named, configured for services). The handler is
    // a SocketsHttpHandler whose ConnectCallback re-resolves DNS on every
    // connection and rejects any address that ProbeAddressGuard flags as
    // private/loopback/link-local/CGNAT/ULA/multicast. This is the
    // authoritative SSRF barrier — URL validation in ServicesController
    // is best-effort because DNS can change between create and probe
    // (DNS rebinding). Auto-redirect is disabled so a 30x to an internal
    // host cannot bypass the guard either.
    var ua = BumpUserAgent.Build(builder.Configuration);
    builder.Services.AddHttpClient("probe", c =>
    {
        c.Timeout = serviceSettings.Timeout;
        c.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
    }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"Probe refused: DNS returned no addresses for {ctx.DnsEndPoint.Host}.");
            }
            foreach (var addr in addresses)
            {
                if (ProbeAddressGuard.IsBlocked(addr))
                {
                    throw new HttpRequestException(
                        $"Probe refused: address {addr} for {ctx.DnsEndPoint.Host} is in a blocked range.");
                }
            }
            var target = addresses[0];
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(target, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    });

    var status = new WorkerStatus();
    builder.Services.AddSingleton(status);
    builder.Services.AddHostedService<AlertWorker>();
    builder.Services.AddHostedService<ServiceProber>();
    builder.Services.AddHostedService<AnnouncementScheduler>();
    builder.Services.AddHostedService<SubscriberSweep>();
    builder.Services.AddHostedService<IdempotencySweep>();

    var app = builder.Build();
    app.UseSerilogRequestLogging();

    // Allow three missed ticks before flipping unhealthy — matches the AlertWorker
    // grace ratio and avoids paging on a single slow probe round.
    var serviceStaleAfter = serviceSettings.Interval * 3;
    var alertStaleAfter = alertSettings.PollInterval * 3;

    app.MapGet("/api/health", () =>
    {
        var now = DateTime.UtcNow;
        var alertFresh = status.LastPollUtc.HasValue
            && status.LastPollUtc.Value > now - alertStaleAfter;
        var serviceFresh = status.LastServiceTickUtc.HasValue
            && status.LastServiceTickUtc.Value > now - serviceStaleAfter;
        var healthy = alertFresh && serviceFresh;

        var response = new
        {
            status = healthy ? "healthy" : "unhealthy",
            lastPollUtc = status.LastPollUtc,
            lastServiceTickUtc = status.LastServiceTickUtc,
            lastAnnouncementTickUtc = status.LastAnnouncementTickUtc,
            lastError = status.LastError
        };

        return healthy ? Results.Ok(response) : Results.Json(response, statusCode: 503);
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Bump.Worker terminated unexpectedly.");
    // Nonzero so the Windows Service manager sees a failed start rather than a clean stop
    // and applies its restart policy. A port conflict or a missing key used to exit 0.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);
}
