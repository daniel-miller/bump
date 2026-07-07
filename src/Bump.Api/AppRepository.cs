using Dapper;
using Npgsql;

namespace Bump.Api;

public sealed class AppRepository(NpgsqlDataSource dataSource)
{
    private const string Cols = """
        SELECT app_key       AS AppKey,
               app_slug      AS AppSlug,
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
                  app_slug      AS AppSlug,
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
        var rows = await conn.QueryAsync<App>(Cols + " ORDER BY app_slug");
        return rows.AsList();
    }

    public async Task<App?> GetBySlugAsync(string slug)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Cols + " WHERE app_slug = @Slug",
            new { Slug = slug });
    }

    public async Task<bool> DeleteBySlugAsync(string slug)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM app WHERE app_slug = @Slug",
            new { Slug = slug });
        return rows > 0;
    }

    public async Task<App> CreateAsync(string slug, string name, int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_slug, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Slug, @Name,
                    @Major, @Minor, @Patch)
            {Returning}
            """,
            new { Slug = slug, Name = name, Major = major, Minor = minor, Patch = patch });
    }

    /// <summary>
    /// Insert-or-update an app by slug. Used for self-registration on API startup.
    /// Version is seeded on first insert only; subsequent boots leave it alone so
    /// PATCH/bump updates are not clobbered.
    /// </summary>
    public async Task<App> UpsertAsync(string slug, string name, int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_slug, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Slug, @Name, @Major, @Minor, @Patch)
            ON CONFLICT (app_slug) DO UPDATE
               SET app_name   = EXCLUDED.app_name,
                   updated_at = CASE
                                  WHEN app.app_name IS DISTINCT FROM EXCLUDED.app_name THEN now()
                                  ELSE app.updated_at
                                END
            {Returning}
            """,
            new { Slug = slug, Name = name, Major = major, Minor = minor, Patch = patch });
    }

    public async Task<App?> UpdateAsync(
        string currentSlug, string newSlug, string name,
        int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            $"""
            UPDATE app
               SET app_slug      = @NewSlug,
                   app_name      = @Name,
                   version_major = @Major,
                   version_minor = @Minor,
                   version_patch = @Patch,
                   updated_at    = now()
             WHERE app_slug      = @CurrentSlug
            {Returning}
            """,
            new
            {
                CurrentSlug = currentSlug,
                NewSlug = newSlug,
                Name = name,
                Major = major,
                Minor = minor,
                Patch = patch
            });
    }

    public async Task<App?> SetVersionAsync(string slug, int? major, int? minor, int? patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            $"""
            UPDATE app
               SET version_major = COALESCE(@Major, version_major),
                   version_minor = COALESCE(@Minor, version_minor),
                   version_patch = COALESCE(@Patch, version_patch),
                   updated_at    = now()
             WHERE app_slug      = @Slug
            {Returning}
            """,
            new { Slug = slug, Major = major, Minor = minor, Patch = patch });
    }

    public async Task<App> IncrementMajorAsync(string slug)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_slug, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Slug, @Slug, 1, 0, 0)
            ON CONFLICT (app_slug) DO UPDATE
               SET version_major = app.version_major + 1,
                   version_minor = 0,
                   version_patch = 0,
                   updated_at    = now()
            {Returning}
            """,
            new { Slug = slug });
    }

    public async Task<App> IncrementMinorAsync(string slug)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_slug, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Slug, @Slug, 0, 1, 0)
            ON CONFLICT (app_slug) DO UPDATE
               SET version_minor = app.version_minor + 1,
                   version_patch = 0,
                   updated_at    = now()
            {Returning}
            """,
            new { Slug = slug });
    }

    public async Task<App> IncrementPatchAsync(string slug)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            $"""
            INSERT INTO app (app_slug, app_name,
                             version_major, version_minor, version_patch)
            VALUES (@Slug, @Slug, 0, 0, 1)
            ON CONFLICT (app_slug) DO UPDATE
               SET version_patch = app.version_patch + 1,
                   updated_at    = now()
            {Returning}
            """,
            new { Slug = slug });
    }
}
