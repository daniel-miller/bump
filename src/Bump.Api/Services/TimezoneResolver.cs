using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace Bump.Api.Services;

/// <summary>
/// Resolves the IANA timezone used for chart day-bucketing and labels.
/// Authenticated users get their own account_timezone (set as a "tz" claim
/// by SessionAuthHandler). Anonymous callers fall back to the admin account's
/// timezone — single-admin model, so there is one canonical "global TZ".
///
/// All returned values are IANA names. Windows IDs are normalized via
/// TimeZoneInfo.TryConvertWindowsIdToIanaId so Postgres AT TIME ZONE works.
/// </summary>
public interface ITimezoneResolver
{
    Task<string> ResolveAsync(ClaimsPrincipal? user, CancellationToken ct = default);
}

public sealed class TimezoneResolver : ITimezoneResolver
{
    private const string CacheKey = "admin-timezone";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IMemoryCache _cache;

    public TimezoneResolver(NpgsqlDataSource dataSource, IMemoryCache cache)
    {
        _dataSource = dataSource;
        _cache = cache;
    }

    public async Task<string> ResolveAsync(ClaimsPrincipal? user, CancellationToken ct = default)
    {
        var raw = user?.FindFirst("tz")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = await GetAdminTimezoneAsync(ct);
        }
        return Normalize(raw);
    }

    private async Task<string> GetAdminTimezoneAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var tz = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT account_timezone FROM account ORDER BY created_at LIMIT 1");
        tz ??= "UTC";
        _cache.Set(CacheKey, tz, CacheTtl);
        return tz;
    }

    private static string Normalize(string id)
    {
        // Postgres AT TIME ZONE only accepts IANA names. If the value is a
        // Windows ID (e.g. "Mountain Standard Time"), translate to its IANA
        // equivalent. Unknown values degrade to UTC rather than failing SQL.
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana))
        {
            return iana;
        }
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return id;
        }
        catch
        {
            return "UTC";
        }
    }
}
