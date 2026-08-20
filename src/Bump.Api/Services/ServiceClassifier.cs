namespace Bump.Api.Services;

public static class ServiceStatuses
{
    public const string Operational = "operational";
    public const string Degraded    = "degraded";
    public const string Down        = "down";
}

public static class ServiceClassifier
{
    /// <summary>
    /// Classifies a probe outcome:
    /// network/timeout/exception → down; HTTP 4xx/5xx → down;
    /// latency &gt; threshold → degraded; HTTP 2xx/3xx → operational.
    /// </summary>
    public static string Classify(int? statusCode, long? latencyMs, bool networkError, int degradedLatencyMs)
    {
        if (networkError) return ServiceStatuses.Down;
        if (statusCode is null) return ServiceStatuses.Down;
        if (statusCode >= 400) return ServiceStatuses.Down;
        if (latencyMs is long ms && ms > degradedLatencyMs) return ServiceStatuses.Degraded;
        return ServiceStatuses.Operational;
    }
}

/// <summary>
/// Decides when a run of failed probes has been confirmed long enough to open an
/// outage. A single failed probe is not enough: transient blips (a CDN edge
/// returning one 5xx, a one-off timeout — routine on GitHub Pages) would open and
/// close incidents several times a day. Only a run of consecutive down probes that
/// reaches the configured threshold counts as a real outage.
/// </summary>
public static class OutagePolicy
{
    /// <summary>
    /// Count of trailing <c>down</c> probes at the end of the history. Any reachable
    /// probe — <c>operational</c> or <c>degraded</c> — breaks the run and resets it to
    /// zero, because a slow response is still a response.
    /// </summary>
    public static int TrailingDownStreak(IReadOnlyList<string> history)
    {
        int n = 0;
        for (int i = history.Count - 1; i >= 0 && history[i] == ServiceStatuses.Down; i--) n++;
        return n;
    }

    /// <summary>
    /// True once the trailing down streak reaches <paramref name="failureThreshold"/>
    /// consecutive probes. A threshold of 1 or less preserves the old behavior of
    /// opening on the first down probe.
    /// </summary>
    public static bool OutageConfirmed(IReadOnlyList<string> history, int failureThreshold)
        => TrailingDownStreak(history) >= Math.Max(1, failureThreshold);
}
