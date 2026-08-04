using Dapper;
using Npgsql;

namespace Bump.Api;

public sealed class AppRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT app_key       AS AppKey,
               app_handle      AS AppHandle,
               app_name      AS AppName,
               version_major AS VersionMajor,
               version_minor AS VersionMinor,
               version_patch AS VersionPatch,
               created_at    AS CreatedAt,
               updated_at    AS UpdatedAt
          FROM app
        """;

    private const string Returning = """
        RETURNING app_key       AS AppKey,
                  app_handle      AS AppHandle,
                  app_name      AS AppName,
                  version_major AS VersionMajor,
                  version_minor AS VersionMinor,
                  version_patch AS VersionPatch,
                  created_at    AS CreatedAt,
                  updated_at    AS UpdatedAt
        """;

    public async Task<IReadOnlyList<App>> GetAllAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<App>(Cols + " ORDER BY app_handle");
        return rows.AsList();
    }

    public async Task<App?> GetByHandleAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Cols + " WHERE app_handle = @Handle",
            new { Handle = handle });
    }

    public async Task<bool> DeleteByHandleAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM app WHERE app_handle = @Handle",
            new { Handle = handle });
        return rows > 0;
    }

    public async Task<App> CreateAsync(string handle, string name, int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_handle, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Handle, @Name,
                    @Major, @Minor, @Patch)
            {Returning}
            """,
            new { Handle = handle, Name = name, Major = major, Minor = minor, Patch = patch });
    }

    /// <summary>
    /// Insert-or-update an app by handle. Used for self-registration on API startup.
    /// Version is seeded on first insert only; subsequent boots leave it alone so
    /// PATCH/bump updates are not clobbered.
    /// </summary>
    public async Task<App> UpsertAsync(string handle, string name, int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_handle, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Handle, @Name, @Major, @Minor, @Patch)
            ON CONFLICT (app_handle) DO UPDATE
               SET app_name   = EXCLUDED.app_name,
                   updated_at = CASE
                                  WHEN app.app_name IS DISTINCT FROM EXCLUDED.app_name THEN now()
                                  ELSE app.updated_at
                                END
            {Returning}
            """,
            new { Handle = handle, Name = name, Major = major, Minor = minor, Patch = patch });
    }

    public async Task<App?> UpdateAsync(
        string currentHandle, string newHandle, string name,
        int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            $"""
            UPDATE app
               SET app_handle      = @NewHandle,
                   app_name      = @Name,
                   version_major = @Major,
                   version_minor = @Minor,
                   version_patch = @Patch,
                   updated_at    = now()
             WHERE app_handle      = @CurrentHandle
            {Returning}
            """,
            new
            {
                CurrentHandle = currentHandle,
                NewHandle = newHandle,
                Name = name,
                Major = major,
                Minor = minor,
                Patch = patch
            });
    }

    public async Task<App?> SetVersionAsync(string handle, int? major, int? minor, int? patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            $"""
            UPDATE app
               SET version_major = COALESCE(@Major, version_major),
                   version_minor = COALESCE(@Minor, version_minor),
                   version_patch = COALESCE(@Patch, version_patch),
                   updated_at    = now()
             WHERE app_handle      = @Handle
            {Returning}
            """,
            new { Handle = handle, Major = major, Minor = minor, Patch = patch });
    }

    public async Task<App> IncrementMajorAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_handle, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Handle, @Handle, 1, 0, 0)
            ON CONFLICT (app_handle) DO UPDATE
               SET version_major = app.version_major + 1,
                   version_minor = 0,
                   version_patch = 0,
                   updated_at    = now()
            {Returning}
            """,
            new { Handle = handle });
    }

    public async Task<App> IncrementMinorAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_handle, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Handle, @Handle, 0, 1, 0)
            ON CONFLICT (app_handle) DO UPDATE
               SET version_minor = app.version_minor + 1,
                   version_patch = 0,
                   updated_at    = now()
            {Returning}
            """,
            new { Handle = handle });
    }

    public async Task<App> IncrementPatchAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_handle, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Handle, @Handle, 0, 0, 1)
            ON CONFLICT (app_handle) DO UPDATE
               SET version_patch = app.version_patch + 1,
                   updated_at    = now()
            {Returning}
            """,
            new { Handle = handle });
    }
}
