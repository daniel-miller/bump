using Dapper;
using Npgsql;

namespace Bump.Api.Services;

public sealed class Board
{
    public int BoardId { get; set; }
    public string BoardSlug { get; set; } = "";
    public string BoardName { get; set; } = "";
    public string? BoardHost { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class BoardRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT tenant_key  AS BoardId,
               tenant_slug AS BoardSlug,
               tenant_name AS BoardName,
               tenant_host AS BoardHost,
               created_at  AS CreatedAt,
               updated_at  AS UpdatedAt
          FROM tenant
        """;

    public async Task<IReadOnlyList<Board>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Board>(Cols + " ORDER BY tenant_name");
        return rows.AsList();
    }

    public async Task<Board?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Board>(Cols + " WHERE tenant_key = @I", new { I = id });
    }

    public async Task<Board?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Board>(
            Cols + " WHERE lower(tenant_slug) = lower(@S)", new { S = slug });
    }

    public async Task<Board?> GetByHostAsync(string host, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Board>(
            Cols + " WHERE lower(tenant_host) = lower(@H)", new { H = host });
    }

    public async Task<Board> CreateAsync(string slug, string name, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<Board>(
            """
            INSERT INTO tenant (tenant_slug, tenant_name) VALUES (@S, @N)
            RETURNING tenant_key  AS BoardId,
                      tenant_slug AS BoardSlug,
                      tenant_name AS BoardName,
                      tenant_host AS BoardHost,
                      created_at  AS CreatedAt,
                      updated_at  AS UpdatedAt
            """,
            new { S = slug.ToLowerInvariant(), N = name });
    }

    public async Task UpdateAsync(int id, string slug, string name, string? host, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE tenant SET tenant_slug = @S, tenant_name = @N, tenant_host = @H, updated_at = now() WHERE tenant_key = @I",
            new { I = id, S = slug.ToLowerInvariant(), N = name, H = host?.ToLowerInvariant() });
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync("DELETE FROM tenant WHERE tenant_key = @I", new { I = id });
        return rows > 0;
    }

    // ---- service<->tenant membership ----
    //
    // The legacy board_service join table is gone. Each service now has
    // exactly one tenant_key (FK on service). The methods below preserve
    // the old call shape while using the single-FK schema:
    //   - SetServicesAsync: re-parents the listed services to @boardId.
    //     Services not in the list are left where they are (we cannot
    //     "detach" a service from a tenant since tenant_key is NOT NULL).
    //   - GetServiceIdsAsync: services currently attached to the tenant.
    //   - GetBoardIdsForServiceAsync: returns the service's single tenant
    //     wrapped in a list to match the old many-to-many signature.

    public async Task SetServicesAsync(int boardId, IEnumerable<int> serviceIds, CancellationToken ct = default)
    {
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE service SET tenant_key = @B WHERE service_key = ANY(@Ids)",
            new { B = boardId, Ids = ids });
    }

    public async Task<IReadOnlyList<int>> GetServiceIdsAsync(int boardId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<int>(
            "SELECT service_key FROM service WHERE tenant_key = @B ORDER BY service_key", new { B = boardId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<int>> GetBoardIdsForServiceAsync(int serviceId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<int>(
            "SELECT tenant_key FROM service WHERE service_key = @M", new { M = serviceId });
        return rows.AsList();
    }
}
