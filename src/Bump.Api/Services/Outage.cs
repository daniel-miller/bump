using Dapper;
using Npgsql;

namespace Bump.Api.Services;

public static class OutageStatuses
{
    public const string Investigating = "investigating";
    public const string Identified    = "identified";
    public const string Monitoring    = "monitoring";
    public const string Resolved      = "resolved";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        Investigating, Identified, Monitoring, Resolved
    };
}

public sealed class Outage
{
    public int OutageId { get; set; }
    public int? ServiceId { get; set; }
    public string OutageTitle { get; set; } = "";
    public string OutageStatus { get; set; } = OutageStatuses.Investigating;
    public string? OutageRegion { get; set; }
    public string? RootCause { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool AutoCreated { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class OutageUpdate
{
    public long UpdateId { get; set; }
    public int OutageId { get; set; }
    public string StatusAtUpdate { get; set; } = "";
    public string UpdateMessage { get; set; } = "";
    public bool Published { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OutageRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT outage_key       AS OutageId,
               service_key      AS ServiceId,
               outage_title     AS OutageTitle,
               outage_status    AS OutageStatus,
               outage_region    AS OutageRegion,
               root_cause       AS RootCause,
               started_at       AS StartedAt,
               resolved_at      AS ResolvedAt,
               auto_created     AS AutoCreated,
               created_by       AS CreatedBy,
               created_at       AS CreatedAt,
               updated_at       AS UpdatedAt
          FROM outage
        """;

    public async Task<IReadOnlyList<Outage>> ListAsync(string? statusFilter, int limit, int offset, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var sql = Cols;
        var args = new DynamicParameters();
        args.Add("Limit", Math.Clamp(limit, 1, 500));
        args.Add("Offset", Math.Max(0, offset));
        if (statusFilter == "open")
        {
            sql += " WHERE outage_status <> 'resolved'";
        }
        sql += " ORDER BY started_at DESC LIMIT @Limit OFFSET @Offset";
        var rows = await conn.QueryAsync<Outage>(sql, args);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Outage>> ListOpenForServicesAsync(IEnumerable<int> serviceIds, CancellationToken ct = default)
    {
        var ids = serviceIds.ToArray();
        if (ids.Length == 0) return Array.Empty<Outage>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Outage>(
            Cols + " WHERE service_key = ANY(@Ids) AND outage_status <> 'resolved' ORDER BY started_at DESC",
            new { Ids = ids });
        return rows.AsList();
    }

    public sealed class DayCount
    {
        public DateTime Day { get; set; }
        public int Count { get; set; }
    }

    public async Task<IReadOnlyList<DayCount>> CountByDayForServicesAsync(IEnumerable<int> serviceIds, int days, string ianaTz, CancellationToken ct = default)
    {
        var ids = serviceIds.ToArray();
        if (ids.Length == 0) return Array.Empty<DayCount>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DayCount>(
            """
            SELECT (date_trunc('day', started_at AT TIME ZONE @Tz))::date AS Day,
                   count(*)::int AS Count
              FROM outage
             WHERE service_key = ANY(@Ids)
               AND started_at >= (date_trunc('day', (now() AT TIME ZONE @Tz)) AT TIME ZONE @Tz)
                                 - (@Days || ' days')::interval
             GROUP BY 1
            """,
            new { Ids = ids, Days = Math.Max(1, days), Tz = ianaTz });
        return rows.AsList();
    }

    public async Task<Outage?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Outage>(Cols + " WHERE outage_key = @I", new { I = id });
    }

    public async Task<Outage?> GetOpenForServiceAsync(int serviceId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Outage>(
            Cols + " WHERE service_key = @M AND outage_status <> 'resolved' ORDER BY started_at DESC LIMIT 1",
            new { M = serviceId });
    }

    public async Task<Outage> CreateAsync(int? serviceId, string title, string? region, string? rootCause, bool autoCreated, Guid? createdBy, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<Outage>(
            """
            INSERT INTO outage (service_key, outage_title, outage_region, root_cause, auto_created, created_by)
            VALUES (@M, @T, @R, @C, @A, @U)
            RETURNING outage_key       AS OutageId,
                      service_key      AS ServiceId,
                      outage_title     AS OutageTitle,
                      outage_status    AS OutageStatus,
                      outage_region    AS OutageRegion,
                      root_cause       AS RootCause,
                      started_at       AS StartedAt,
                      resolved_at      AS ResolvedAt,
                      auto_created     AS AutoCreated,
                      created_by       AS CreatedBy,
                      created_at       AS CreatedAt,
                      updated_at       AS UpdatedAt
            """,
            new { M = serviceId, T = title, R = region, C = rootCause, A = autoCreated, U = createdBy });
    }

    public async Task UpdateAsync(int id, string title, string? region, string? rootCause, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE outage SET outage_title = @T, outage_region = @R, root_cause = @C, updated_at = now()
             WHERE outage_key = @I
            """,
            new { I = id, T = title, R = region, C = rootCause });
    }

    public async Task<OutageUpdate> AppendUpdateAsync(int outageId, string status, string message, bool published, Guid? createdBy, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var u = await conn.QuerySingleAsync<OutageUpdate>(
            """
            INSERT INTO outage_update (outage_key, update_status, update_message, published, created_by)
            VALUES (@I, @S, @M, @P, @U)
            RETURNING update_key       AS UpdateId,
                      outage_key       AS OutageId,
                      update_status    AS StatusAtUpdate,
                      update_message   AS UpdateMessage,
                      published        AS Published,
                      created_by       AS CreatedBy,
                      created_at       AS CreatedAt
            """,
            new { I = outageId, S = status, M = message, P = published, U = createdBy }, tx);

        var resolved = status == OutageStatuses.Resolved;
        await conn.ExecuteAsync(
            """
            UPDATE outage
               SET outage_status = @S,
                   resolved_at = CASE WHEN @S = 'resolved' THEN now() ELSE resolved_at END,
                   updated_at = now()
             WHERE outage_key = @I
            """,
            new { I = outageId, S = status }, tx);

        await tx.CommitAsync(ct);
        return u;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM outage_update WHERE outage_key = @I", new { I = id }, tx);
        var rows = await conn.ExecuteAsync("DELETE FROM outage WHERE outage_key = @I", new { I = id }, tx);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<OutageUpdate>> GetUpdatesAsync(int outageId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutageUpdate>(
            """
            SELECT update_key       AS UpdateId,
                   outage_key       AS OutageId,
                   update_status    AS StatusAtUpdate,
                   update_message   AS UpdateMessage,
                   published        AS Published,
                   created_by       AS CreatedBy,
                   created_at       AS CreatedAt
              FROM outage_update
             WHERE outage_key = @I
             ORDER BY created_at
            """,
            new { I = outageId });
        return rows.AsList();
    }
}
