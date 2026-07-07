using Dapper;
using Npgsql;

namespace Bump.Api.Auth;

public sealed class EmailChangeToken
{
    public long TokenId { get; set; }
    public Guid UserId { get; set; }
    public string NewEmail { get; set; } = string.Empty;
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EmailChangeTokenRepository(NpgsqlDataSource dataSource)
{
    public async Task CreateAsync(Guid userId, string newEmail, byte[] tokenHash, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO account_email_change (account_id, new_email, change_hash, expires_at)
            VALUES (@Id, @Email, @Hash, @Exp)
            """,
            new { Id = userId, Email = newEmail, Hash = tokenHash, Exp = expiresAt });
    }

    /// <summary>
    /// Atomically marks the matching unused, unexpired token as used and
    /// returns the row. Returns null when no token matches.
    /// </summary>
    public async Task<EmailChangeToken?> ConsumeAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EmailChangeToken>(
            """
            UPDATE account_email_change
               SET used_at = now()
             WHERE change_hash = @Hash
               AND used_at IS NULL
               AND expires_at > now()
            RETURNING change_key AS TokenId,
                      account_id    AS UserId,
                      new_email  AS NewEmail,
                      change_hash AS TokenHash,
                      expires_at AS ExpiresAt,
                      used_at    AS UsedAt,
                      created_at AS CreatedAt
            """,
            new { Hash = tokenHash });
    }
}
