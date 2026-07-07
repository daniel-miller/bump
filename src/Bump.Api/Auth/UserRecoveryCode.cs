using Dapper;
using Npgsql;

namespace Bump.Api.Auth;

public sealed class UserRecoveryCode
{
    public long CodeId { get; set; }
    public Guid UserId { get; set; }
    public byte[] CodeHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserRecoveryCodeRepository(NpgsqlDataSource dataSource)
{
    public async Task ReplaceAllAsync(Guid userId, IEnumerable<byte[]> codeHashes, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM account_recovery WHERE account_id = @Id",
            new { Id = userId }, tx);
        foreach (var hash in codeHashes)
        {
            await conn.ExecuteAsync(
                "INSERT INTO account_recovery (account_id, recovery_hash) VALUES (@Id, @Hash)",
                new { Id = userId, Hash = hash }, tx);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<bool> ConsumeAsync(Guid userId, byte[] codeHash, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            """
            UPDATE account_recovery
               SET used_at = now()
             WHERE account_id = @Id
               AND recovery_hash = @Hash
               AND used_at IS NULL
            """,
            new { Id = userId, Hash = codeHash });
        return rows > 0;
    }
}
