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
