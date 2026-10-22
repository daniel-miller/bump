using System.Security.Cryptography;
using Dapper;
using Npgsql;

namespace Bump.Api.Services;

public sealed class Subscriber
{
    public int SubscriberId { get; set; }
    public int OwnerId { get; set; }
    public string SubscriberEmail { get; set; } = "";
    public byte[] ConfirmToken { get; set; } = Array.Empty<byte>();
    public byte[] UnsubscribeToken { get; set; } = Array.Empty<byte>();
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SubscriberRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT subscriber_key    AS SubscriberId,
               owner_key         AS OwnerId,
               subscriber_email  AS SubscriberEmail,
               confirm_token     AS ConfirmToken,
               unsubscribe_token AS UnsubscribeToken,
               confirmed_at      AS ConfirmedAt,
               created_at        AS CreatedAt
          FROM subscriber
        """;

    // Confirm tokens are emailed once and stored hashed.
    public static (string Token, byte[] Hash) NewToken()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var token = EncodeToken(raw);
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return (token, hash);
    }

    public static byte[] HashToken(string token) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    // Unsubscribe tokens are stored as issued, not hashed: every outage, recovery and
    // announcement email rebuilds the link from the stored value. When they were stored
    // hashed, those links carried the hash, the endpoint hashed it again, and nothing
    // matched, so only the link in the confirmation email ever worked.
    public static (string Token, byte[] Raw) NewUnsubscribeToken()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        return (EncodeToken(raw), raw);
    }

    /// <summary>Base64url without padding, the form a token takes in a link.</summary>
    public static string EncodeToken(byte[] raw) =>
        Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>The bytes a link's token stands for, or null when it is not base64url.</summary>
    public static byte[]? DecodeToken(string token)
    {
        var s = token.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }

    public async Task<Subscriber?> CreateOrGetAsync(int ownerId, string email, byte[] confirmHash, byte[] unsubRaw, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Subscriber>(
            """
            INSERT INTO subscriber (owner_key, subscriber_email, confirm_token, unsubscribe_token)
            VALUES (@B, @E, @C, @U)
            ON CONFLICT (owner_key, lower(subscriber_email)) DO NOTHING
            RETURNING subscriber_key    AS SubscriberId,
                      owner_key         AS OwnerId,
                      subscriber_email  AS SubscriberEmail,
                      confirm_token     AS ConfirmToken,
                      unsubscribe_token AS UnsubscribeToken,
                      confirmed_at      AS ConfirmedAt,
                      created_at        AS CreatedAt
            """,
            new { B = ownerId, E = email, C = confirmHash, U = unsubRaw });
    }

    public async Task<Subscriber?> ConfirmAsync(byte[] confirmHash, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Subscriber>(
            """
            UPDATE subscriber SET confirmed_at = now()
             WHERE confirm_token = @H AND confirmed_at IS NULL
            RETURNING subscriber_key    AS SubscriberId,
                      owner_key         AS OwnerId,
                      subscriber_email  AS SubscriberEmail,
                      confirm_token     AS ConfirmToken,
                      unsubscribe_token AS UnsubscribeToken,
                      confirmed_at      AS ConfirmedAt,
                      created_at        AS CreatedAt
            """,
            new { H = confirmHash });
    }

    public async Task<bool> UnsubscribeAsync(byte[] unsubRaw, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM subscriber WHERE unsubscribe_token = @U",
            new { U = unsubRaw });
        return rows > 0;
    }

    public async Task<IReadOnlyList<Subscriber>> ListConfirmedAsync(int ownerId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Subscriber>(
            Cols + " WHERE owner_key = @B AND confirmed_at IS NOT NULL ORDER BY created_at",
            new { B = ownerId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Subscriber>> ListAllForOwnerAsync(int ownerId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Subscriber>(
            Cols + " WHERE owner_key = @B ORDER BY created_at", new { B = ownerId });
        return rows.AsList();
    }

    public async Task<int> CountForOwnerAsync(int ownerId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM subscriber WHERE owner_key = @B",
            new { B = ownerId });
    }

    public async Task<bool> AdminDeleteAsync(int subscriberId, int ownerId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM subscriber WHERE subscriber_key = @I AND owner_key = @B",
            new { I = subscriberId, B = ownerId });
        return rows > 0;
    }

    public async Task PurgeUnconfirmedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM subscriber WHERE confirmed_at IS NULL AND created_at < now() - (@D || ' seconds')::interval",
            new { D = (int)olderThan.TotalSeconds });
    }
}
