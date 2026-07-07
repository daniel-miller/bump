using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Bump.Api;

/// <summary>
/// Persistence for the idempotency-key cache. Records are partitioned by a
/// SHA-256 hash of the bearer token so one caller's keys cannot collide
/// with another's — and so the raw token is never written to storage.
/// </summary>
public sealed class IdempotencyRepository(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// How long a stored response stays replayable. Matches the common
    /// industry default — long enough to cover retry storms, short enough
    /// that clients can safely reuse keys after a day.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<IdempotencyRecord?> GetAsync(byte[] apiKeyHash, string idempotencyKey)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        return await conn.QuerySingleOrDefaultAsync<IdempotencyRecord>(
            """
            SELECT bearer_token_hash      AS ApiKeyHash,
                   idempotency_code      AS IdempotencyKey,
                   request_fingerprint   AS RequestFingerprint,
                   response_status       AS ResponseStatus,
                   response_content_type AS ResponseContentType,
                   response_body         AS ResponseBody
              FROM idempotency
             WHERE bearer_token_hash = @ApiKeyHash
               AND idempotency_code  = @IdempotencyKey
               AND expires_at        > now()
            """,
            new { ApiKeyHash = apiKeyHash, IdempotencyKey = idempotencyKey });
    }

    /// <summary>
    /// Insert a response record. On conflict (another request with the same
    /// key raced us), silently drop — the first writer wins, which matches
    /// replay semantics on subsequent retries.
    /// </summary>
    public async Task StoreAsync(
        byte[] apiKeyHash,
        string idempotencyKey,
        byte[] requestFingerprint,
        int responseStatus,
        string? responseContentType,
        byte[] responseBody)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO idempotency
                (bearer_token_hash, idempotency_code, request_fingerprint,
                 response_status, response_content_type, response_body,
                 expires_at)
            VALUES
                (@bearer_token_hash, @idempotency_code, @request_fingerprint,
                 @response_status, @response_content_type, @response_body,
                 @expires_at)
            ON CONFLICT (bearer_token_hash, idempotency_code) DO NOTHING
            """,
            conn);

        cmd.Parameters.AddWithValue("bearer_token_hash", apiKeyHash);
        cmd.Parameters.AddWithValue("idempotency_code", idempotencyKey);
        cmd.Parameters.AddWithValue("request_fingerprint", requestFingerprint);
        cmd.Parameters.AddWithValue("response_status", responseStatus);
        cmd.Parameters.AddWithValue(
            "response_content_type",
            (object?)responseContentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("response_body", responseBody);
        cmd.Parameters.Add(new NpgsqlParameter("expires_at", NpgsqlDbType.TimestampTz)
        {
            Value = DateTimeOffset.UtcNow.Add(Ttl)
        });

        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class IdempotencyRecord
{
    public byte[] ApiKeyHash { get; set; } = Array.Empty<byte>();
    public string IdempotencyKey { get; set; } = string.Empty;
    public byte[] RequestFingerprint { get; set; } = Array.Empty<byte>();
    public int ResponseStatus { get; set; }
    public string? ResponseContentType { get; set; }
    public byte[] ResponseBody { get; set; } = Array.Empty<byte>();
}
