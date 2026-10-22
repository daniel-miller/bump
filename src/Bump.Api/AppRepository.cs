using Dapper;
using Npgsql;

namespace Bump.Api;

public sealed class AppRepository(NpgsqlDataSource dataSource)
{
    private const string Select = """
        SELECT a.app_key         AS AppKey,
               a.app_handle      AS AppHandle,
               a.app_name        AS AppName,
               a.app_description AS AppDescription,
               o.owner_handle    AS OwnerHandle,
               a.app_aliases     AS AppAliases,
               a.version_major   AS VersionMajor,
               a.version_minor   AS VersionMinor,
               a.version_patch   AS VersionPatch,
               a.created_at      AS CreatedAt,
               a.updated_at      AS UpdatedAt
        """;

    private const string FromApp = """

          FROM app a
          JOIN owner o ON o.owner_key = a.owner_key
        """;

    // The app a token names, as its current handle or a former one (an
    // alias). Reporters keep sending an old handle until their repo switches,
    // so problem reports and version calls resolve through this. The current
    // handle wins if a token is somehow both.
    private const string ResolveKey = """
        (SELECT app_key
           FROM app
          WHERE app_handle = @Handle OR @Handle = ANY (app_aliases)
          ORDER BY app_handle = @Handle DESC
          LIMIT 1)
        """;

    // RETURNING cannot join, and every app carries its owner's handle, so
    // writes return their row through a CTE.
    private static string Returning(string dml) => $"""
        WITH a AS (
        {dml}
        RETURNING *
        )
        {Select}
          FROM a
          JOIN owner o ON o.owner_key = a.owner_key
        """;

    public async Task<IReadOnlyList<App>> GetAllAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<App>(Select + FromApp + " ORDER BY a.app_handle");
        return rows.AsList();
    }

    /// <summary>
    /// The app with this current handle. Admin screens and deletes address an
    /// app by the name it has now, never by an alias.
    /// </summary>
    public async Task<App?> GetByHandleAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Select + FromApp + " WHERE a.app_handle = @Handle",
            new { Handle = handle });
    }

    /// <summary>The app answering to this token as its handle or an alias.</summary>
    public async Task<App?> ResolveAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Select + FromApp + $" WHERE a.app_key = {ResolveKey}",
            new { Handle = handle });
    }

    /// <summary>
    /// The first token that some app other than <paramref name="exceptKey"/>
    /// already answers to, as a handle or an alias; null when all are free.
    /// A unique index covers handles alone, so this is what stops an alias from
    /// shadowing another app's handle, or a new handle from hijacking an alias.
    /// </summary>
    public async Task<string?> FindTakenAsync(IEnumerable<string> tokens, int exceptKey = 0)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<string>(
            """
            SELECT t
              FROM unnest(@Tokens::text[]) AS t
             WHERE EXISTS (SELECT 1
                             FROM app
                            WHERE app_key <> @Except
                              AND (app_handle = t OR t = ANY (app_aliases)))
             LIMIT 1
            """,
            new { Tokens = tokens.ToArray(), Except = exceptKey });
    }

    /// <summary>
    /// Deletes by current handle. Throws a restrict violation while any
    /// service still belongs to the app; callers answer that with 409.
    /// </summary>
    public async Task<bool> DeleteByHandleAsync(string handle)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM app WHERE app_handle = @Handle",
            new { Handle = handle });
        return rows > 0;
    }

    /// <summary>Inserts an app. The caller has already checked the owner exists.</summary>
    public async Task<App> CreateAsync(string handle, string name, string owner, int major, int minor, int patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<App>(
            Returning("""
                INSERT INTO app (app_handle, app_name, owner_key,
                                 version_major, version_minor, version_patch)
                VALUES (@Handle, @Name,
                        (SELECT owner_key FROM owner WHERE owner_handle = @Owner),
                        @Major, @Minor, @Patch)
                """),
            new { Handle = handle, Name = name, Owner = owner, Major = major, Minor = minor, Patch = patch });
    }

    /// <summary>
    /// Updates by current handle. A null owner or alias list leaves that value
    /// unchanged; the caller has already checked a named owner exists.
    /// </summary>
    public async Task<App?> UpdateAsync(
        string currentHandle, string newHandle, string name,
        int major, int minor, int patch,
        string? owner, string[]? aliases)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Returning("""
                UPDATE app
                   SET app_handle    = @NewHandle,
                       app_name      = @Name,
                       owner_key     = COALESCE((SELECT owner_key FROM owner WHERE owner_handle = @Owner), owner_key),
                       app_aliases   = COALESCE(@Aliases::text[], app_aliases),
                       version_major = @Major,
                       version_minor = @Minor,
                       version_patch = @Patch,
                       updated_at    = now()
                 WHERE app_handle    = @CurrentHandle
                """),
            new
            {
                CurrentHandle = currentHandle,
                NewHandle = newHandle,
                Name = name,
                Owner = owner,
                Aliases = aliases,
                Major = major,
                Minor = minor,
                Patch = patch
            });
    }

    public async Task<App?> SetVersionAsync(string handle, int? major, int? minor, int? patch)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Returning($"""
                UPDATE app
                   SET version_major = COALESCE(@Major, version_major),
                       version_minor = COALESCE(@Minor, version_minor),
                       version_patch = COALESCE(@Patch, version_patch),
                       updated_at    = now()
                 WHERE app_key = {ResolveKey}
                """),
            new { Handle = handle, Major = major, Minor = minor, Patch = patch });
    }

    // The increments used to insert a missing app at the matching seed. An app
    // now needs an owner, which a build script bumping a version cannot know,
    // so they update an existing app and return null for an unknown handle.
    // (An upsert would not do either: Postgres checks NOT NULL on the proposed
    // row before it looks for the conflicting one, so it fails for every app.)

    public Task<App?> IncrementMajorAsync(string handle) => IncrementAsync(handle, """
        version_major = version_major + 1,
        version_minor = 0,
        version_patch = 0
        """);

    public Task<App?> IncrementMinorAsync(string handle) => IncrementAsync(handle, """
        version_minor = version_minor + 1,
        version_patch = 0
        """);

    public Task<App?> IncrementPatchAsync(string handle) => IncrementAsync(handle, """
        version_patch = version_patch + 1
        """);

    private async Task<App?> IncrementAsync(string handle, string set)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            Returning($"""
                UPDATE app
                   SET {set},
                       updated_at = now()
                 WHERE app_key = {ResolveKey}
                """),
            new { Handle = handle });
    }
}
