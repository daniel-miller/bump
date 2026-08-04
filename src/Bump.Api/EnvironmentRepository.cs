using Dapper;
using Npgsql;

namespace Bump.Api;

public sealed class EnvironmentRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT environment_key         AS EnvironmentKey,
               environment_number      AS EnvironmentNumber,
               environment_handle        AS EnvironmentHandle,
               environment_name        AS EnvironmentName,
               environment_description AS EnvironmentDescription,
               environment_aliases     AS EnvironmentAliases,
               is_special_purpose      AS IsSpecialPurpose,
               is_derived_from_live    AS IsDerivedFromLive,
               created_at              AS CreatedAt,
               updated_at              AS UpdatedAt
          FROM environment
        """;

    public async Task<IReadOnlyList<EnvironmentRecord>> GetAllAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<EnvironmentRecord>(Cols + " ORDER BY environment_number NULLS LAST, environment_key");
        return rows.AsList();
    }
}
