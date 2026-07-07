using System.Data;
using System.Net;
using System.Net.Sockets;
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
using Serilog.Events;

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

    // Serilog configured in code (per-project log path). Shared
    // appsettings.json holds Bump:* keys only; logging stays here.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .ReadFrom.Services(services)
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("tmp/logs/bump-worker-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    var connectionString = builder.Configuration.GetConnectionString("Bump");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:Bump is empty. Set it via config/appsettings.work.json or the ConnectionStrings__Bump environment variable.");
    }
    var dataSource = NpgsqlDataSource.Create(connectionString);
    builder.Services.AddSingleton(dataSource);

    builder.Services.AddSingleton<ServiceRepository>();
    builder.Services.AddSingleton<OutageRepository>();
    builder.Services.AddSingleton<BoardRepository>();
    builder.Services.AddSingleton<AnnouncementRepository>();
    builder.Services.AddSingleton<SubscriberRepository>();

    var mailgun = new MailgunOptions
    {
        ApiKey = builder.Configuration["Bump:Mailgun:ApiKey"] ?? "",
        Domain = builder.Configuration["Bump:Mailgun:Domain"] ?? "",
        From = builder.Configuration["Bump:Mailgun:From"] ?? "Bump <noreply@example.com>",
        Region = builder.Configuration["Bump:Mailgun:Region"] ?? "us"
    };
    if (string.IsNullOrWhiteSpace(mailgun.ApiKey))
    {
        throw new InvalidOperationException(
            "Bump:Mailgun:ApiKey is empty. Set it via config/appsettings.work.json or the Bump__Mailgun__ApiKey environment variable.");
    }
    if (string.IsNullOrWhiteSpace(mailgun.Domain))
    {
        throw new InvalidOperationException(
            "Bump:Mailgun:Domain is empty. Set it via config/appsettings.work.json or the Bump__Mailgun__Domain environment variable.");
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
        c.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Bump:Services:TimeoutSeconds", 5));
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

    var pollSeconds = app.Configuration.GetValue("Bump:Alerts:PollSeconds", 300);
    var serviceIntervalSeconds = app.Configuration.GetValue("Bump:Services:IntervalSeconds", 60);
    // Allow three missed ticks before flipping unhealthy — matches the AlertWorker
    // grace ratio and avoids paging on a single slow probe round.
    var serviceStaleAfter = TimeSpan.FromSeconds(serviceIntervalSeconds * 3);
    var alertStaleAfter = TimeSpan.FromSeconds(pollSeconds * 3);

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
