using Npgsql;

namespace Bump.Worker.Idempotency;

/// <summary>
/// Hourly purge of idempotency rows whose 24h TTL has elapsed.
/// </summary>
public sealed class IdempotencySweep : BackgroundService
{
    private readonly ILogger<IdempotencySweep> _logger;
    private readonly NpgsqlDataSource _dataSource;

    public IdempotencySweep(ILogger<IdempotencySweep> logger, NpgsqlDataSource dataSource)
    {
        _logger = logger;
        _dataSource = dataSource;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var cmd = _dataSource.CreateCommand("DELETE FROM idempotency WHERE expires_at < now()");
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idempotency sweep failed.");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }
}
