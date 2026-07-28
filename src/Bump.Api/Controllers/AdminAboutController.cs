using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Bump.Api.Auth;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/admin/about")]
[Tags("About")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class AdminAboutController : ControllerBase
{
    private const string RepositoryUrl = "https://github.com/daniel-miller/bump";

    private readonly NpgsqlDataSource _db;
    private readonly AppRepository _apps;
    private readonly IHostEnvironment _env;
    private readonly ReleaseSettings _release;

    public AdminAboutController(NpgsqlDataSource db, AppRepository apps, IHostEnvironment env, ReleaseSettings release)
    {
        _db = db;
        _apps = apps;
        _env = env;
        _release = release;
    }

    /// <summary>About payload for the admin UI. Reports build/runtime/database info and registry counts.</summary>
    [HttpGet("", Name = "adminAbout")]
    [ProducesResponseType(typeof(AboutResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var bump = await _apps.GetBySlugAsync("bump");
        var assembly = typeof(AdminAboutController).Assembly;
        var assemblyName = assembly.GetName();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var build = new BuildInfo(
            Version: bump?.Version ?? "0.0.0",
            AssemblyVersion: assemblyName.Version?.ToString() ?? "0.0.0",
            InformationalVersion: informational,
            FileVersion: fileVersion,
            BuildDate: SafeFileLastWriteTime(assembly.Location)
        );

        var proc = Process.GetCurrentProcess();
        var now = DateTimeOffset.UtcNow;
        var server = new ServerInfo(
            MachineName: Environment.MachineName,
            // Two separate facts. ReleaseEnvironment is the deployment label ops sets;
            // EnvironmentName is ASPNETCORE_ENVIRONMENT, which switches app behaviour.
            // Showing both makes a mismatch visible instead of guessing which one is wrong.
            ReleaseEnvironment: _release.Environment,
            EnvironmentName: _env.EnvironmentName,
            ProcessId: proc.Id,
            ProcessStartedAt: proc.StartTime.ToUniversalTime(),
            ProcessUptime: (now - new DateTimeOffset(proc.StartTime.ToUniversalTime(), TimeSpan.Zero)).ToString(@"d\.hh\:mm\:ss"),
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            WorkingSetBytes: Environment.WorkingSet,
            GcTotalMemoryBytes: GC.GetTotalMemory(forceFullCollection: false),
            ServerTime: now,
            Timezone: TimeZoneInfo.Local.Id
        );

        await using var conn = await _db.OpenConnectionAsync(ct);

        // Pull all counts and database metadata in one connection. Keep each
        // query trivial — the About page is read-mostly and should not be a
        // performance hot spot, but there is no reason to be wasteful either.
        // Npgsql returns timestamptz as DateTime (Kind=Utc). Dapper's scalar
        // and property binders cannot cast that to DateTimeOffset, so read
        // PG timestamps as DateTime and wrap them ourselves.
        var pgVersion = await conn.QuerySingleAsync<string>(new CommandDefinition("SELECT version()", cancellationToken: ct));
        var pgNowDt = await conn.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT now()", cancellationToken: ct));
        var pgNow = new DateTimeOffset(DateTime.SpecifyKind(pgNowDt, DateTimeKind.Utc), TimeSpan.Zero);
        var latest = await conn.QuerySingleOrDefaultAsync<MigrationRow>(new CommandDefinition(
            "SELECT name AS Name, applied_at AS AppliedAt FROM _migration_history ORDER BY applied_at DESC, name DESC LIMIT 1",
            cancellationToken: ct));
        var migrationCount = await conn.QuerySingleAsync<long>(new CommandDefinition("SELECT count(*) FROM _migration_history", cancellationToken: ct));

        var counts = await conn.QuerySingleAsync<RegistryRow>(new CommandDefinition(
            """
            SELECT
                (SELECT count(*) FROM app)                                                            AS Apps,
                (SELECT count(*) FROM environment)                                                    AS Environments,
                (SELECT count(*) FROM environment WHERE is_special_purpose)                           AS SpecialEnvironments,
                (SELECT count(*) FROM tenant)                                                         AS Tenants,
                (SELECT count(*) FROM service)                                                        AS Services,
                (SELECT count(*) FROM service WHERE service_paused)                                   AS ServicesPaused,
                (SELECT count(*) FROM outage WHERE resolved_at IS NULL)                               AS OpenOutages,
                (SELECT count(*) FROM outage WHERE started_at >= now() - interval '7 days')           AS Outages7d,
                (SELECT count(DISTINCT problem_fingerprint) FROM problem
                  WHERE resolved_at IS NULL AND reported_at >= now() - interval '24 hours')           AS OpenProblemsDistinct24h,
                (SELECT count(*) FROM problem
                  WHERE reported_at >= now() - interval '24 hours')                                   AS ProblemReports24h,
                (SELECT count(*) FROM problem
                  WHERE reported_at >= now() - interval '7 days')                                     AS ProblemReports7d,
                (SELECT count(*) FROM account)                                                        AS Accounts,
                (SELECT count(*) FROM announcement
                  WHERE (publish_at IS NULL OR publish_at <= now())
                    AND (auto_hide_at IS NULL OR auto_hide_at > now()))                               AS ActiveAnnouncements,
                (SELECT count(*) FROM probe_event WHERE checked_at >= now() - interval '24 hours')    AS ProbeEvents24h
            """, cancellationToken: ct));

        var database = new DatabaseInfo(
            Version: pgVersion,
            ServerTime: pgNow,
            Host: TryGetHost(_db.ConnectionString),
            DatabaseName: TryGetDatabase(_db.ConnectionString),
            LatestMigration: latest?.Name,
            LatestMigrationAt: latest is null ? null : new DateTimeOffset(DateTime.SpecifyKind(latest.AppliedAt, DateTimeKind.Utc), TimeSpan.Zero),
            MigrationCount: migrationCount
        );

        var registry = new RegistryInfo(
            Apps: counts.Apps,
            Environments: counts.Environments,
            SpecialEnvironments: counts.SpecialEnvironments,
            Tenants: counts.Tenants,
            Services: counts.Services,
            ServicesPaused: counts.ServicesPaused,
            OpenOutages: counts.OpenOutages,
            Outages7d: counts.Outages7d,
            OpenProblemsDistinct24h: counts.OpenProblemsDistinct24h,
            ProblemReports24h: counts.ProblemReports24h,
            ProblemReports7d: counts.ProblemReports7d,
            Accounts: counts.Accounts,
            ActiveAnnouncements: counts.ActiveAnnouncements,
            ProbeEvents24h: counts.ProbeEvents24h
        );

        var payload = new AboutResponse(
            Repository: new RepositoryInfo(Url: RepositoryUrl),
            Build: build,
            Server: server,
            Database: database,
            Registry: registry
        );

        return JsonResults.Ok(payload, ignoreNulls: true).AsAction();
    }

    private static DateTimeOffset? SafeFileLastWriteTime(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        return new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
    }

    private static string? TryGetHost(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try { return new NpgsqlConnectionStringBuilder(connectionString).Host; }
        catch { return null; }
    }

    private static string? TryGetDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try { return new NpgsqlConnectionStringBuilder(connectionString).Database; }
        catch { return null; }
    }

    private sealed class MigrationRow
    {
        public string Name { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; }
    }

    private sealed class RegistryRow
    {
        public long Apps { get; set; }
        public long Environments { get; set; }
        public long SpecialEnvironments { get; set; }
        public long Tenants { get; set; }
        public long Services { get; set; }
        public long ServicesPaused { get; set; }
        public long OpenOutages { get; set; }
        public long Outages7d { get; set; }
        public long OpenProblemsDistinct24h { get; set; }
        public long ProblemReports24h { get; set; }
        public long ProblemReports7d { get; set; }
        public long Accounts { get; set; }
        public long ActiveAnnouncements { get; set; }
        public long ProbeEvents24h { get; set; }
    }
}

public sealed record AboutResponse(
    RepositoryInfo Repository,
    BuildInfo Build,
    ServerInfo Server,
    DatabaseInfo Database,
    RegistryInfo Registry
);

public sealed record RepositoryInfo(string Url);

public sealed record BuildInfo(
    string Version,
    string AssemblyVersion,
    string? InformationalVersion,
    string? FileVersion,
    DateTimeOffset? BuildDate
);

public sealed record ServerInfo(
    string MachineName,
    string ReleaseEnvironment,
    string EnvironmentName,
    int ProcessId,
    DateTimeOffset ProcessStartedAt,
    string ProcessUptime,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    long WorkingSetBytes,
    long GcTotalMemoryBytes,
    DateTimeOffset ServerTime,
    string Timezone
);

public sealed record DatabaseInfo(
    string Version,
    DateTimeOffset ServerTime,
    string? Host,
    string? DatabaseName,
    string? LatestMigration,
    DateTimeOffset? LatestMigrationAt,
    long MigrationCount
);

public sealed record RegistryInfo(
    long Apps,
    long Environments,
    long SpecialEnvironments,
    long Tenants,
    long Services,
    long ServicesPaused,
    long OpenOutages,
    long Outages7d,
    long OpenProblemsDistinct24h,
    long ProblemReports24h,
    long ProblemReports7d,
    long Accounts,
    long ActiveAnnouncements,
    long ProbeEvents24h
);
