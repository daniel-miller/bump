using System.Text.RegularExpressions;
using Dapper;
using Newtonsoft.Json;
using Npgsql;

namespace Bump.Api.Services;

public sealed class Service
{
    public int ServiceId { get; set; }
    public string ServiceSlug { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string ServiceUrl { get; set; } = "";
    public string ServiceTenant { get; set; } = "";
    public string ServiceEnvironment { get; set; } = "";
    public bool ServicePaused { get; set; }
    public bool IsPrivate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class ServiceState
{
    public int ServiceId { get; set; }
    public string HistoryJson { get; set; } = "[]";
    public int LatencyMs { get; set; }
    public decimal UptimePct { get; set; } = 100.00m;
    public string LastStatus { get; set; } = ServiceStatuses.Operational;
    public DateTimeOffset? LastCheckAt { get; set; }
    public DateTimeOffset? LastOutageAt { get; set; }

    public List<string> History
    {
        get => JsonConvert.DeserializeObject<List<string>>(HistoryJson) ?? new List<string>();
        set => HistoryJson = JsonConvert.SerializeObject(value);
    }
}

public static class ServiceSlugRules
{
    private static readonly Regex SlugPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    // Tenant/environment slugs: allow both '-' and '_' as separators.
    private static readonly Regex TagSlugPattern = new(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);

    public static IResult? ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return JsonResults.UnprocessableEntity("Invalid slug", "Slug is required.");
        if (slug.Length > 60)
            return JsonResults.UnprocessableEntity("Invalid slug", "Slug must be 60 characters or fewer.");
        if (!SlugPattern.IsMatch(slug))
            return JsonResults.UnprocessableEntity("Invalid slug", "Slug must contain only lowercase letters, digits, and single hyphens.");
        return null;
    }

    public static IResult? ValidateTagSlug(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return JsonResults.UnprocessableEntity($"Invalid {fieldName}", $"{fieldName} is required.");
        if (value.Length > 60)
            return JsonResults.UnprocessableEntity($"Invalid {fieldName}", $"{fieldName} must be 60 characters or fewer.");
        if (!TagSlugPattern.IsMatch(value))
            return JsonResults.UnprocessableEntity($"Invalid {fieldName}", $"{fieldName} must contain only lowercase letters, digits, hyphens, and underscores.");
        return null;
    }
}

public sealed class ServiceRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT s.service_key      AS ServiceId,
               s.service_slug     AS ServiceSlug,
               s.service_name     AS ServiceName,
               s.service_url      AS ServiceUrl,
               t.tenant_slug      AS ServiceTenant,
               e.environment_slug AS ServiceEnvironment,
               s.service_paused   AS ServicePaused,
               s.is_private       AS IsPrivate,
               s.created_at       AS CreatedAt,
               s.updated_at       AS UpdatedAt
          FROM service s
          JOIN tenant      t ON t.tenant_key      = s.tenant_key
          JOIN environment e ON e.environment_key = s.environment_key
        """;

    public async Task<IReadOnlyList<Service>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Service>(Cols + " ORDER BY s.service_name");
        return rows.AsList();
    }

    public async Task<Service?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Service>(Cols + " WHERE s.service_slug = @S", new { S = slug });
    }

    public async Task<Service?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Service>(Cols + " WHERE s.service_key = @I", new { I = id });
    }

    public async Task<IReadOnlyList<Service>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Service>(Cols + " WHERE s.service_paused = false");
        return rows.AsList();
    }

    public async Task<Service> CreateAsync(string slug, string name, string url, string tenant, string environment, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // app_key defaults to the self-registered "bump" app since the legacy
        // create-service surface did not carry an app identifier.
        var service = await conn.QuerySingleAsync<Service>(
            """
            WITH ins AS (
                INSERT INTO service (service_slug, service_name, service_url, tenant_key, environment_key, app_key)
                VALUES (
                    @Slug, @Name, @Url,
                    (SELECT tenant_key      FROM tenant      WHERE tenant_slug      = @Tenant),
                    (SELECT environment_key FROM environment
                      WHERE environment_slug = @Environment
                         OR @Environment = ANY(environment_aliases)
                      LIMIT 1),
                    (SELECT app_key         FROM app         WHERE app_slug         = 'bump')
                )
                RETURNING service_key, service_slug, service_name, service_url,
                          tenant_key, environment_key, service_paused, is_private, created_at, updated_at
            )
            SELECT i.service_key      AS ServiceId,
                   i.service_slug     AS ServiceSlug,
                   i.service_name     AS ServiceName,
                   i.service_url      AS ServiceUrl,
                   t.tenant_slug      AS ServiceTenant,
                   e.environment_slug AS ServiceEnvironment,
                   i.service_paused   AS ServicePaused,
                   i.is_private       AS IsPrivate,
                   i.created_at       AS CreatedAt,
                   i.updated_at       AS UpdatedAt
              FROM ins i
              JOIN tenant      t ON t.tenant_key      = i.tenant_key
              JOIN environment e ON e.environment_key = i.environment_key
            """,
            new { Slug = slug, Name = name, Url = url, Tenant = tenant, Environment = environment }, tx);

        // Pre-fill the history with operational so the UI bar isn't blank.
        var history = JsonConvert.SerializeObject(Enumerable.Repeat(ServiceStatuses.Operational, 60));
        await conn.ExecuteAsync(
            "INSERT INTO service_state (service_key, history_json) VALUES (@Id, @H::jsonb)",
            new { Id = service.ServiceId, H = history }, tx);

        await tx.CommitAsync(ct);
        return service;
    }

    public async Task UpdateAsync(string slug, string name, string url, string tenant, string environment, bool isPrivate, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE service
               SET service_name    = @Name,
                   service_url     = @Url,
                   tenant_key      = (SELECT tenant_key      FROM tenant      WHERE tenant_slug      = @Tenant),
                   environment_key = (SELECT environment_key FROM environment
                                       WHERE environment_slug = @Environment
                                          OR @Environment = ANY(environment_aliases)
                                       LIMIT 1),
                   is_private      = @IsPrivate,
                   updated_at      = now()
             WHERE service_slug = @Slug
            """, new { Slug = slug, Name = name, Url = url, Tenant = tenant, Environment = environment, IsPrivate = isPrivate });
    }

    public async Task SetPrivateAsync(string slug, bool isPrivate, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE service SET is_private = @P, updated_at = now() WHERE service_slug = @S",
            new { S = slug, P = isPrivate });
    }

    public async Task SetPausedAsync(string slug, bool paused, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE service SET service_paused = @P, updated_at = now() WHERE service_slug = @S",
            new { S = slug, P = paused });
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync("DELETE FROM service WHERE service_slug = @S", new { S = slug });
        return rows > 0;
    }

    // ------- State -------

    public async Task<ServiceState?> GetStateAsync(int serviceId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ServiceState>(
            """
            SELECT service_key       AS ServiceId,
                   history_json::text AS HistoryJson,
                   latency_ms        AS LatencyMs,
                   uptime_pct        AS UptimePct,
                   last_status       AS LastStatus,
                   last_check_at     AS LastCheckAt,
                   last_outage_at    AS LastOutageAt
              FROM service_state
             WHERE service_key = @Id
            """, new { Id = serviceId });
    }

    public async Task<IReadOnlyDictionary<int, ServiceState>> GetStatesAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var list = ids.ToArray();
        if (list.Length == 0) return new Dictionary<int, ServiceState>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ServiceState>(
            """
            SELECT service_key       AS ServiceId,
                   history_json::text AS HistoryJson,
                   latency_ms        AS LatencyMs,
                   uptime_pct        AS UptimePct,
                   last_status       AS LastStatus,
                   last_check_at     AS LastCheckAt,
                   last_outage_at    AS LastOutageAt
              FROM service_state
             WHERE service_key = ANY(@Ids)
            """, new { Ids = list });
        return rows.ToDictionary(r => r.ServiceId);
    }

    public async Task UpsertStateAsync(int serviceId, string historyJson, int latencyMs, decimal uptimePct, string lastStatus, DateTimeOffset lastCheckAt, DateTimeOffset? lastOutageAt, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO service_state (service_key, history_json, latency_ms, uptime_pct, last_status, last_check_at, last_outage_at)
            VALUES (@Id, @H::jsonb, @L, @U, @S, @C, @In)
            ON CONFLICT (service_key) DO UPDATE
               SET history_json   = EXCLUDED.history_json,
                   latency_ms     = EXCLUDED.latency_ms,
                   uptime_pct     = EXCLUDED.uptime_pct,
                   last_status    = EXCLUDED.last_status,
                   last_check_at  = EXCLUDED.last_check_at,
                   last_outage_at = COALESCE(EXCLUDED.last_outage_at, service_state.last_outage_at)
            """,
            new { Id = serviceId, H = historyJson, L = latencyMs, U = uptimePct, S = lastStatus, C = lastCheckAt, In = lastOutageAt });
    }

    // ------- Daily metrics -------

    public sealed class DailyRow
    {
        public int ServiceId { get; set; }
        public DateTime Day { get; set; }
        public int Probes { get; set; }
        public int OperationalCount { get; set; }
        public long LatencySumMs { get; set; }
    }

    // DateOnly (not DateTime) so Npgsql binds straight to PG `date` without
    // going through `timestamptz`. A `DateTime` with `Kind=Utc` would be sent
    // as `timestamptz` and PG would cast it to `date` using the session
    // `timezone` GUC, shifting UTC-midnight probes into the previous local
    // day's row when the server is west of UTC.
    public async Task IncrementDailyAsync(int serviceId, DateOnly day, bool isOperational, long latencyMs, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO service_daily (service_key, day, probes, operational_count, latency_sum_ms)
            VALUES (@Id, @D, 1, @O, @L)
            ON CONFLICT (service_key, day) DO UPDATE
               SET probes            = service_daily.probes + 1,
                   operational_count = service_daily.operational_count + EXCLUDED.operational_count,
                   latency_sum_ms    = service_daily.latency_sum_ms + EXCLUDED.latency_sum_ms
            """,
            new { Id = serviceId, D = day, O = isOperational ? 1 : 0, L = latencyMs });
    }

    public async Task PruneOldDailyAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM service_daily WHERE day < (current_date - interval '30 days')");
    }

    // ------- Raw probe events (TZ-flexible aggregation) -------

    public async Task InsertProbeEventAsync(int serviceId, bool isOperational, int latencyMs, string probeStatus, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO probe_event (service_key, is_operational, latency_ms, probe_status)
            VALUES (@Id, @Op, @L, @S)
            """,
            new { Id = serviceId, Op = isOperational, L = latencyMs, S = probeStatus });
    }

    public async Task PruneOldProbeEventsAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM probe_event WHERE checked_at < now() - interval '30 days'");
    }

    // DailyRow.Day reflects the calendar day in the requested IANA timezone.
    // Bucketing happens at read time via (checked_at AT TIME ZONE @Tz), so the
    // same raw probe rows can power charts in any account's display timezone.
    public async Task<IReadOnlyList<DailyRow>> GetDailyAsync(int serviceId, int days, string ianaTz, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DailyRow>(
            """
            SELECT service_key                                                AS ServiceId,
                   (date_trunc('day', checked_at AT TIME ZONE @Tz))::date    AS Day,
                   count(*)::int                                             AS Probes,
                   count(*) FILTER (WHERE is_operational)::int               AS OperationalCount,
                   coalesce(sum(latency_ms), 0)::bigint                      AS LatencySumMs
              FROM probe_event
             WHERE service_key = @Id
               AND checked_at >= (date_trunc('day', (now() AT TIME ZONE @Tz)) AT TIME ZONE @Tz)
                                 - make_interval(days => @Days)
             GROUP BY 1, 2
             ORDER BY 2
            """,
            new { Id = serviceId, Days = days, Tz = ianaTz });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<DailyRow>> GetDailyForBoardAsync(IEnumerable<int> serviceIds, int days, string ianaTz, CancellationToken ct = default)
    {
        var ids = serviceIds.ToArray();
        if (ids.Length == 0) return Array.Empty<DailyRow>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DailyRow>(
            """
            SELECT service_key                                                AS ServiceId,
                   (date_trunc('day', checked_at AT TIME ZONE @Tz))::date    AS Day,
                   count(*)::int                                             AS Probes,
                   count(*) FILTER (WHERE is_operational)::int               AS OperationalCount,
                   coalesce(sum(latency_ms), 0)::bigint                      AS LatencySumMs
              FROM probe_event
             WHERE service_key = ANY(@Ids)
               AND checked_at >= (date_trunc('day', (now() AT TIME ZONE @Tz)) AT TIME ZONE @Tz)
                                 - make_interval(days => @Days)
             GROUP BY 1, 2
             ORDER BY 2
            """,
            new { Ids = ids, Days = days, Tz = ianaTz });
        return rows.AsList();
    }
}
