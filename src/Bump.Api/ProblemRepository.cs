using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;

namespace Bump.Api;

public class ProblemRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ProblemRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<long> InsertAsync(ProblemReportPayload payload)
    {
        var fingerprint = Fingerprint.Compute(payload);
        var extensions = payload.Extensions != null
            ? JsonConvert.SerializeObject(payload.Extensions)
            : null;
        var exception = payload.Exception != null
            ? JsonConvert.SerializeObject(payload.Exception)
            : null;

        await using var conn = await _dataSource.OpenConnectionAsync();

        // Resolve app_key and environment_key independently — a problem can
        // belong to any registered (app, environment) pair, no matching
        // service row required. Each must exist on its own; missing either
        // surfaces as a 422 instead of letting a NOT NULL violation explode
        // as a 500.
        // Environment lookup accepts the canonical slug *or* any alias listed
        // in environment_aliases so clients can keep sending "production",
        // "qa", "local", etc. without us forcing the canonical name on them.
        const string resolveSql = """
            SELECT
                (SELECT app_key         FROM app         WHERE app_slug         = @app_slug),
                (SELECT environment_key FROM environment
                  WHERE environment_slug = @environment
                     OR @environment = ANY(environment_aliases)
                  LIMIT 1)
            """;

        int appKey;
        int environmentKey;
        await using (var resolveCmd = new NpgsqlCommand(resolveSql, conn))
        {
            resolveCmd.Parameters.AddWithValue("app_slug", payload.Application);
            resolveCmd.Parameters.AddWithValue("environment", EnvironmentTokens.Resolve(payload.Environment));
            await using var reader = await resolveCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var appResolved      = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
            var envResolved      = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            if (appResolved is null) throw new UnknownAppException(payload.Application);
            if (envResolved is null) throw new UnknownEnvironmentException(payload.Environment);
            appKey         = appResolved.Value;
            environmentKey = envResolved.Value;
        }

        const string sql = """
            INSERT INTO problem
                (problem_fingerprint, problem_type, problem_title, problem_status, problem_detail,
                 problem_instance, problem_extensions,
                 app_key, environment_key,
                 problem_exception, account_id, account_email)
            VALUES
                (@fingerprint, @type, @title, @status, @detail,
                 @instance, @extensions::jsonb,
                 @app_key, @environment_key,
                 @exception::jsonb, @account_id, @account_email)
            RETURNING problem_key
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("fingerprint", fingerprint);
        cmd.Parameters.AddWithValue("type", payload.Type);
        cmd.Parameters.AddWithValue("title", payload.Title);
        cmd.Parameters.AddWithValue("status", (object?)payload.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("detail", (object?)payload.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("instance", (object?)payload.Instance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("extensions", (object?)extensions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("app_key", appKey);
        cmd.Parameters.AddWithValue("environment_key", environmentKey);
        cmd.Parameters.AddWithValue("exception", (object?)exception ?? DBNull.Value);
        cmd.Parameters.AddWithValue("account_id", payload.UserId.HasValue ? (object)payload.UserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("account_email", (object?)payload.UserEmail ?? DBNull.Value);

        var key = (long)(await cmd.ExecuteScalarAsync())!;
        return key;
    }

    public sealed class UnknownAppException : Exception
    {
        public string AppSlug { get; }
        public UnknownAppException(string appSlug)
            : base($"No app registered with slug '{appSlug}'.")
        {
            AppSlug = appSlug;
        }
    }

    public sealed class UnknownEnvironmentException : Exception
    {
        public string Environment { get; }
        public UnknownEnvironmentException(string environment)
            : base($"No environment registered with slug '{environment}'.")
        {
            Environment = environment;
        }
    }

    public async Task<List<ProblemReportRecord>> QueryAsync(ProblemReportFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (filter.Environment != null)
        {
            conditions.Add("(e.environment_slug = @environment OR @environment = ANY(e.environment_aliases))");
            parameters.Add(new NpgsqlParameter("environment", EnvironmentTokens.Resolve(filter.Environment)));
        }

        if (filter.AppSlug != null)
        {
            conditions.Add("a.app_slug = @app_slug");
            parameters.Add(new NpgsqlParameter("app_slug", filter.AppSlug));
        }

        if (filter.Fingerprint != null)
        {
            conditions.Add("p.problem_fingerprint = @fingerprint");
            parameters.Add(new NpgsqlParameter("fingerprint", filter.Fingerprint));
        }

        if (filter.From != null)
        {
            conditions.Add("p.reported_at >= @from");
            parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = filter.From });
        }

        if (filter.To != null)
        {
            conditions.Add("p.reported_at <= @to");
            parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = filter.To });
        }

        if (!filter.IncludeResolved)
        {
            conditions.Add("p.resolved_at IS NULL");
        }

        var where = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        var sql = $"""
            SELECT {SelectColumns}
            FROM problem p
            JOIN app         a ON a.app_key         = p.app_key
            JOIN environment e ON e.environment_key = p.environment_key
            {where}
            ORDER BY p.reported_at DESC
            LIMIT @limit OFFSET @offset
            """;

        parameters.Add(new NpgsqlParameter("limit", filter.Limit));
        parameters.Add(new NpgsqlParameter("offset", filter.Offset));

        await using var conn = await _dataSource.OpenConnectionAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        var results = new List<ProblemReportRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(Hydrate(reader));
        }

        return results;
    }

    private const string SelectColumns = """
        p.problem_key, p.problem_fingerprint, p.reported_at, p.dispatched_at, p.resolved_at,
        p.problem_type, p.problem_title, p.problem_status, p.problem_detail,
        p.problem_instance, p.problem_extensions::text,
        e.environment_slug, e.environment_name, e.environment_description,
        a.app_slug, a.app_name, a.version_major, a.version_minor, a.version_patch,
        p.problem_exception::text,
        p.account_id, p.account_email
        """;

    private static ProblemReportRecord Hydrate(Npgsql.NpgsqlDataReader reader) =>
        new()
        {
            ProblemKey             = reader.GetInt64(0),
            Fingerprint            = reader.GetString(1),
            ReportedAt             = reader.GetDateTime(2),
            DispatchedAt           = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            ResolvedAt             = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            Type                   = reader.GetString(5),
            Title                  = reader.GetString(6),
            Status                 = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            Detail                 = reader.IsDBNull(8) ? null : reader.GetString(8),
            Instance               = reader.IsDBNull(9) ? null : reader.GetString(9),
            Extensions             = reader.IsDBNull(10) ? null : reader.GetString(10),
            Environment            = reader.GetString(11),
            EnvironmentName        = reader.GetString(12),
            EnvironmentDescription = reader.IsDBNull(13) ? null : reader.GetString(13),
            AppSlug                = reader.GetString(14),
            AppName                = reader.GetString(15),
            AppVersion             = $"{reader.GetInt32(16)}.{reader.GetInt32(17)}.{reader.GetInt32(18)}",
            Exception              = reader.IsDBNull(19)
                ? null
                : JsonConvert.DeserializeObject<ExceptionInfo>(reader.GetString(19)),
            UserId                 = reader.IsDBNull(20) ? null : reader.GetGuid(20),
            UserEmail              = reader.IsDBNull(21) ? null : reader.GetString(21),
        };

    public async Task<ProblemReportRecord?> GetByIdAsync(long problemKey)
    {
        var sql = $"""
            SELECT {SelectColumns}
            FROM problem p
            JOIN app         a ON a.app_key         = p.app_key
            JOIN environment e ON e.environment_key = p.environment_key
            WHERE p.problem_key = @key
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", problemKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return Hydrate(reader);
    }

    public async Task<bool> SetResolvedAsync(long problemKey, bool resolved)
    {
        const string sql = "UPDATE problem SET resolved_at = @ts WHERE problem_key = @key";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", problemKey);
        cmd.Parameters.Add(new NpgsqlParameter("ts", NpgsqlDbType.TimestampTz)
        {
            Value = resolved ? (object)DateTimeOffset.UtcNow : DBNull.Value,
        });

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long problemKey)
    {
        const string sql = "DELETE FROM problem WHERE problem_key = @key";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", problemKey);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public sealed class ActiveCounts
    {
        public int Total { get; set; }
        public int DistinctFingerprints { get; set; }
    }

    public sealed class DayCount
    {
        public DateTime Day { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Per-day total of problem reports (resolved + unresolved) across the
    /// last <paramref name="days"/> days. Days with zero reports are omitted —
    /// the caller fills gaps when shaping the response.
    /// </summary>
    public async Task<IReadOnlyList<DayCount>> CountByDayAsync(int days, string ianaTz, CancellationToken ct = default)
    {
        const string sql = """
            SELECT date_trunc('day', reported_at AT TIME ZONE @tz)::date AS day,
                   count(*)::int                                         AS cnt
              FROM problem
             WHERE reported_at >= (date_trunc('day', (now() AT TIME ZONE @tz)) AT TIME ZONE @tz)
                                  - make_interval(days => @days)
             GROUP BY 1
             ORDER BY 1
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("days", days);
        cmd.Parameters.AddWithValue("tz", ianaTz);

        var rows = new List<DayCount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new DayCount { Day = reader.GetDateTime(0), Count = reader.GetInt32(1) });
        }
        return rows;
    }

    /// <summary>
    /// Count unresolved problems received within the window, across all
    /// (app, environment) pairs — no service scope. Use this for the global
    /// admin KPI: problems exist independently of service registration, so
    /// scoping by serviceIds would hide problems for unmonitored apps.
    /// </summary>
    public async Task<ActiveCounts> CountAllActiveAsync(TimeSpan window, CancellationToken ct = default)
    {
        const string sql = """
            SELECT count(*)::int                              AS total,
                   count(DISTINCT problem_fingerprint)::int   AS distinct_fp
              FROM problem
             WHERE reported_at >= now() - @window
               AND resolved_at IS NULL
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("window", NpgsqlDbType.Interval) { Value = window });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new ActiveCounts();
        return new ActiveCounts { Total = reader.GetInt32(0), DistinctFingerprints = reader.GetInt32(1) };
    }

    public async Task<ActiveCounts> CountActiveAsync(IEnumerable<int> serviceIds, TimeSpan window, CancellationToken ct = default)
    {
        var ids = serviceIds.ToArray();
        if (ids.Length == 0) return new ActiveCounts();

        // Problems live in (app, environment) space, not service space. Map the
        // caller's service ids to their (app_key, environment_key) pairs and
        // count problems that match any of those pairs in the window.
        const string sql = """
            SELECT count(*)::int                                AS total,
                   count(DISTINCT p.problem_fingerprint)::int   AS distinct_fp
              FROM problem p
              JOIN service s
                ON s.app_key         = p.app_key
               AND s.environment_key = p.environment_key
             WHERE s.service_key = ANY(@ids)
               AND p.reported_at >= now() - @window
               AND p.resolved_at IS NULL
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.Add(new NpgsqlParameter("window", NpgsqlDbType.Interval) { Value = window });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new ActiveCounts();
        return new ActiveCounts { Total = reader.GetInt32(0), DistinctFingerprints = reader.GetInt32(1) };
    }

}
