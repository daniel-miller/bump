using Dapper;
using Npgsql;

namespace Bump.Api;

public sealed class ServerRecord
{
    public int ServerKey { get; set; }
    public short? ServerNumber { get; set; }
    public string ServerHandle { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string? ServerDescription { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed record ServerResponse(
    int ServerKey,
    short? ServerNumber,
    string ServerHandle,
    string ServerName,
    string? ServerDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public static ServerResponse From(ServerRecord s) => new(
        s.ServerKey,
        s.ServerNumber,
        s.ServerHandle,
        s.ServerName,
        s.ServerDescription,
        s.CreatedAt,
        s.UpdatedAt
    );
}

public sealed class ServerRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT server_key         AS ServerKey,
               server_number      AS ServerNumber,
               server_handle      AS ServerHandle,
               server_name        AS ServerName,
               server_description AS ServerDescription,
               created_at         AS CreatedAt,
               updated_at         AS UpdatedAt
          FROM server
        """;

    public async Task<IReadOnlyList<ServerRecord>> GetAllAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ServerRecord>(Cols + " ORDER BY server_number NULLS LAST, server_handle");
        return rows.AsList();
    }
}
