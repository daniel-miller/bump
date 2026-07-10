using Npgsql;

namespace Bump.Api.Migrations;

/// <summary>
/// Applies db/migrations/*.sql files in lexicographic order, recording each by filename
/// in <c>_migration_history</c>. Each file runs in its own transaction. Files
/// must be idempotent (CREATE ... IF NOT EXISTS) so this is safe to re-run
/// against an existing database.
///
/// Bump.Api owns the migrations; Bump.Worker assumes the API has already
/// migrated.
/// </summary>
public sealed class Migrator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<Migrator> _logger;
    private readonly string _dbDirectory;

    public Migrator(NpgsqlDataSource dataSource, ILogger<Migrator> logger, IHostEnvironment env)
    {
        _dataSource = dataSource;
        _logger = logger;

        // Look next to the binary first (production layout: COPY db ./db),
        // then walk up to find the repo's db/migrations/ folder (developer layout).
        _dbDirectory = ResolveDbDirectory(env);
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        if (_dbDirectory is null || !Directory.Exists(_dbDirectory))
        {
            _logger.LogWarning("Migrations directory not found; skipping. Searched near {Base}", AppContext.BaseDirectory);
            return;
        }

        await EnsureHistoryTableAsync(ct);
        var applied = await LoadAppliedAsync(ct);

        var files = Directory.EnumerateFiles(_dbDirectory, "*.sql")
            .Where(p => !Path.GetFileName(p).StartsWith("seed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (applied.Contains(name))
            {
                continue;
            }

            var sql = await File.ReadAllTextAsync(file, ct);
            _logger.LogInformation("Applying migration {Name}", name);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var record = new NpgsqlCommand(
                "INSERT INTO _migration_history(name, applied_at) VALUES (@n, now())", conn, tx))
            {
                record.Parameters.AddWithValue("n", name);
                await record.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }

    private async Task EnsureHistoryTableAsync(CancellationToken ct)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS _migration_history (
                name        text        PRIMARY KEY,
                applied_at  timestamptz NOT NULL DEFAULT now()
            );";
        await using var cmd = _dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<HashSet<string>> LoadAppliedAsync(CancellationToken ct)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = _dataSource.CreateCommand("SELECT name FROM _migration_history");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied.Add(reader.GetString(0));
        }
        return applied;
    }

    private static string ResolveDbDirectory(IHostEnvironment env)
    {
        // Probe ContentRoot/db/migrations, BaseDirectory/db/migrations, then
        // walk parents looking for db/migrations/004-create-app.sql as a sentinel.
        var candidates = new List<string>
        {
            Path.Combine(env.ContentRootPath, "db", "migrations"),
            Path.Combine(AppContext.BaseDirectory, "db", "migrations"),
        };

        var dir = new DirectoryInfo(env.ContentRootPath);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "db", "migrations"));
        }

        foreach (var path in candidates)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "004-create-app.sql")))
            {
                return path;
            }
        }
        return candidates[0];
    }
}
