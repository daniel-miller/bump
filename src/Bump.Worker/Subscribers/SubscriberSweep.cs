using Bump.Api.Services;

namespace Bump.Worker.Subscribers;

/// <summary>
/// Daily cleanup of subscribers who never confirmed within 7 days.
/// </summary>
public sealed class SubscriberSweep : BackgroundService
{
    private readonly ILogger<SubscriberSweep> _logger;
    private readonly SubscriberRepository _subscribers;

    public SubscriberSweep(ILogger<SubscriberSweep> logger, SubscriberRepository subscribers)
    {
        _logger = logger;
        _subscribers = subscribers;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run on startup, then once per day.
        var interval = TimeSpan.FromHours(24);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _subscribers.PurgeUnconfirmedAsync(TimeSpan.FromDays(7), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscriber sweep failed.");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }
}
