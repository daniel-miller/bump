using Dapper;
using Npgsql;

namespace Bump.Api.Services;

public sealed class Owner
{
    public int OwnerId { get; set; }
    public short? OwnerNumber { get; set; }
    public string OwnerHandle { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string? OwnerDescription { get; set; }
    public string? OwnerHost { get; set; }
    public string? OwnerTheme { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class OwnerRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT owner_key         AS OwnerId,
               owner_number      AS OwnerNumber,
               owner_handle        AS OwnerHandle,
               owner_name        AS OwnerName,
               owner_description AS OwnerDescription,
               owner_host        AS OwnerHost,
               owner_theme::text AS OwnerTheme,
               created_at        AS CreatedAt,
               updated_at        AS UpdatedAt
          FROM owner
        """;

    // Roster order: numbered owners first in roster position, ad hoc owners
    // (no number) after them alphabetically.
    public async Task<IReadOnlyList<Owner>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Owner>(Cols + " ORDER BY owner_number NULLS LAST, owner_name");
        return rows.AsList();
    }

    public async Task<Owner?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Owner>(Cols + " WHERE owner_key = @I", new { I = id });
    }

    public async Task<Owner?> GetByHandleAsync(string handle, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Owner>(
            Cols + " WHERE lower(owner_handle) = lower(@S)", new { S = handle });
    }

    public async Task<Owner?> GetByHostAsync(string host, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Owner>(
            Cols + " WHERE lower(owner_host) = lower(@H)", new { H = host });
    }

    public async Task<Owner> CreateAsync(string handle, string name, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<Owner>(
            """
            INSERT INTO owner (owner_handle, owner_name) VALUES (@S, @N)
            RETURNING owner_key         AS OwnerId,
                      owner_number      AS OwnerNumber,
                      owner_handle        AS OwnerHandle,
                      owner_name        AS OwnerName,
                      owner_description AS OwnerDescription,
                      owner_host        AS OwnerHost,
                      owner_theme::text AS OwnerTheme,
                      created_at        AS CreatedAt,
                      updated_at        AS UpdatedAt
            """,
            new { S = handle.ToLowerInvariant(), N = name });
    }

    public async Task UpdateAsync(int id, string handle, string name, string? host, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE owner SET owner_handle = @S, owner_name = @N, owner_host = @H, updated_at = now() WHERE owner_key = @I",
            new { I = id, S = handle.ToLowerInvariant(), N = name, H = host?.ToLowerInvariant() });
    }

    public async Task UpdateThemeAsync(int id, string? themeJson, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE owner SET owner_theme = @T::jsonb, updated_at = now() WHERE owner_key = @I",
            new { I = id, T = themeJson });
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync("DELETE FROM owner WHERE owner_key = @I", new { I = id });
        return rows > 0;
    }

    // ---- service<->owner membership ----
    //
    // Each service has exactly one owner_key (FK on service). The methods
    // below preserve the membership call shape over that single-FK schema:
    //   - SetServicesAsync: re-parents the listed services to @ownerId.
    //     Services not in the list are left where they are (we cannot
    //     "detach" a service from an owner since owner_key is NOT NULL).
    //   - GetServiceIdsAsync: services currently attached to the owner.
    //   - GetOwnerIdsForServiceAsync: returns the service's single owner
    //     wrapped in a list to match the old many-to-many signature.

    public async Task SetServicesAsync(int ownerId, IEnumerable<int> serviceIds, CancellationToken ct = default)
    {
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE service SET owner_key = @B WHERE service_key = ANY(@Ids)",
            new { B = ownerId, Ids = ids });
    }

    public async Task<IReadOnlyList<int>> GetServiceIdsAsync(int ownerId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<int>(
            "SELECT service_key FROM service WHERE owner_key = @B ORDER BY service_key", new { B = ownerId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<int>> GetOwnerIdsForServiceAsync(int serviceId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<int>(
            "SELECT owner_key FROM service WHERE service_key = @M", new { M = serviceId });
        return rows.AsList();
    }
}
