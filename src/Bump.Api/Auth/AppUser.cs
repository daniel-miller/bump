using Dapper;
using Npgsql;

namespace Bump.Api.Auth;

public sealed class AppUser
{
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserTimezone { get; set; } = "UTC";
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public string PasswordAlg { get; set; } = "argon2id";
    public byte[]? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AppUserRepository(NpgsqlDataSource dataSource)
{
    private const string SelectColumns = """
        SELECT account_id            AS UserId,
               account_email         AS UserEmail,
               account_full_name     AS UserFullName,
               account_timezone      AS UserTimezone,
               password_hash      AS PasswordHash,
               password_salt      AS PasswordSalt,
               password_alg       AS PasswordAlg,
               totp_secret        AS TotpSecret,
               totp_enabled       AS TotpEnabled,
               account_roles         AS Roles,
               failed_login_count AS FailedLoginCount,
               locked_until       AS LockedUntil,
               created_at         AS CreatedAt,
               updated_at         AS UpdatedAt
          FROM account
        """;

    public async Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AppUser>(
            SelectColumns + " WHERE lower(account_email) = lower(@Email)",
            new { Email = email });
    }

    public async Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AppUser>(
            SelectColumns + " WHERE account_id = @Id",
            new { Id = userId });
    }

    public async Task<AppUser> CreateAsync(string email, string fullName, byte[] hash, byte[] salt, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AppUser>(
            """
            INSERT INTO account (account_email, account_full_name, password_hash, password_salt)
            VALUES (@Email, @FullName, @Hash, @Salt)
            RETURNING account_id            AS UserId,
                      account_email         AS UserEmail,
                      account_full_name     AS UserFullName,
                      account_timezone      AS UserTimezone,
                      password_hash      AS PasswordHash,
                      password_salt      AS PasswordSalt,
                      password_alg       AS PasswordAlg,
                      totp_secret        AS TotpSecret,
                      totp_enabled       AS TotpEnabled,
                      account_roles         AS Roles,
                      failed_login_count AS FailedLoginCount,
                      locked_until       AS LockedUntil,
                      created_at         AS CreatedAt,
                      updated_at         AS UpdatedAt
            """,
            new { Email = email, FullName = fullName, Hash = hash, Salt = salt });
    }

    public async Task UpdateProfileAsync(Guid userId, string fullName, string timezone, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE account
               SET account_full_name = @FullName,
                   account_timezone  = @Timezone,
                   updated_at     = now()
             WHERE account_id        = @Id
            """,
            new { Id = userId, FullName = fullName, Timezone = timezone });
    }

    public async Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE account
               SET account_email = @Email,
                   updated_at = now()
             WHERE account_id    = @Id
            """,
            new { Id = userId, Email = email });
    }

    public async Task UpdatePasswordAsync(Guid userId, byte[] hash, byte[] salt, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE account SET password_hash = @Hash, password_salt = @Salt, updated_at = now()
             WHERE account_id = @Id
            """,
            new { Id = userId, Hash = hash, Salt = salt });
    }

    public async Task SetTotpAsync(Guid userId, byte[]? secret, bool enabled, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE account SET totp_secret = @Secret, totp_enabled = @Enabled, updated_at = now()
             WHERE account_id = @Id
            """,
            new { Id = userId, Secret = (object?)secret ?? DBNull.Value, Enabled = enabled });
    }

    /// <summary>
    /// Increment the per-account failed-login counter and, when the new
    /// value crosses <paramref name="threshold"/>, set <c>locked_until</c>
    /// to <c>now() + lockDuration</c>. Both updates run in a single SQL
    /// statement to avoid a TOCTOU between the read and the write.
    /// </summary>
    public async Task<(int FailedCount, DateTimeOffset? LockedUntil)> RecordFailedLoginAsync(
        Guid userId, int threshold, TimeSpan lockDuration, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(int FailedCount, DateTimeOffset? LockedUntil)>(
            """
            UPDATE account
               SET failed_login_count = failed_login_count + 1,
                   locked_until = CASE
                       WHEN failed_login_count + 1 >= @Threshold
                           THEN now() + (@LockSeconds || ' seconds')::interval
                       ELSE locked_until
                   END,
                   updated_at = now()
             WHERE account_id = @Id
            RETURNING failed_login_count AS FailedCount,
                      locked_until       AS LockedUntil
            """,
            new { Id = userId, Threshold = threshold, LockSeconds = (int)lockDuration.TotalSeconds });
        return row;
    }

    public async Task ResetFailedLoginAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE account
               SET failed_login_count = 0,
                   locked_until       = NULL,
                   updated_at         = now()
             WHERE account_id = @Id
               AND (failed_login_count <> 0 OR locked_until IS NOT NULL)
            """,
            new { Id = userId });
    }
}
