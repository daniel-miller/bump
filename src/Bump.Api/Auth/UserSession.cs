using Dapper;
using Npgsql;

namespace Bump.Api.Auth;

public sealed class UserSession
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class UserSessionRepository(NpgsqlDataSource dataSource)
{
    public async Task<UserSession> CreateAsync(Guid sessionId, Guid userId, byte[] tokenHash, DateTimeOffset expiresAt, string? userAgent, string? ip, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<UserSession>(
            """
            INSERT INTO account_session (session_id, account_id, bearer_token_hash, expires_at, user_agent, user_ip)
            VALUES (@SessionId, @UserId, @TokenHash, @ExpiresAt, @UserAgent, @Ip::inet)
            RETURNING session_id  AS SessionId,
                      account_id  AS UserId,
                      bearer_token_hash  AS TokenHash,
                      created_at  AS CreatedAt,
                      expires_at  AS ExpiresAt,
                      revoked_at  AS RevokedAt,
                      user_agent  AS UserAgent
            """,
            new { SessionId = sessionId, UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt, UserAgent = userAgent, Ip = ip });
    }

    public async Task<UserSession?> GetActiveAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserSession>(
            """
            SELECT session_id  AS SessionId,
                   account_id  AS UserId,
                   bearer_token_hash  AS TokenHash,
                   created_at  AS CreatedAt,
                   expires_at  AS ExpiresAt,
                   revoked_at  AS RevokedAt,
                   user_agent  AS UserAgent
              FROM account_session
             WHERE session_id  = @Id
               AND revoked_at IS NULL
               AND expires_at  > now()
            """,
            new { Id = sessionId });
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE account_session SET revoked_at = now() WHERE session_id = @Id",
            new { Id = sessionId });
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE account_session SET revoked_at = now() WHERE account_id = @Id AND revoked_at IS NULL",
            new { Id = userId });
    }
}
