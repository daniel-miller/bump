using Dapper;
using Npgsql;

namespace Bump.Api.Auth;

public sealed class PasswordResetToken
{
    public long TokenId { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PasswordResetTokenRepository(NpgsqlDataSource dataSource)
{
    public async Task CreateAsync(Guid userId, byte[] tokenHash, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "INSERT INTO account_password_change (account_id, change_hash, expires_at) VALUES (@Id, @Hash, @Exp)",
            new { Id = userId, Hash = tokenHash, Exp = expiresAt });
    }

    public async Task<PasswordResetToken?> ConsumeAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PasswordResetToken>(
            """
            UPDATE account_password_change
               SET used_at = now()
             WHERE change_hash = @Hash
               AND used_at IS NULL
               AND expires_at > now()
            RETURNING change_key AS TokenId,
                      account_id    AS UserId,
                      change_hash AS TokenHash,
                      expires_at AS ExpiresAt,
                      used_at    AS UsedAt,
                      created_at AS CreatedAt
            """,
            new { Hash = tokenHash });
    }
}
