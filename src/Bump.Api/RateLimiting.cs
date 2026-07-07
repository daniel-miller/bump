using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Bump.Api;

/// <summary>
/// Per-API-key rate limiting. The goal is to keep one badly-behaved caller
/// — or a buggy client in an infinite loop — from crowding out the rest of
/// the API.
///
/// Partitioning is by the Authorization bearer token, so consumers with
/// distinct keys have distinct buckets. Requests missing a bearer token
/// fall into a shared "anonymous" bucket; the auth filter will reject them
/// anyway, but the limiter protects us from a flood of unauthenticated
/// probes.
///
/// Limits on rejection are intentionally shaped for publishing:
///   - 429 responses carry <c>application/problem+json</c> with a
///     <c>Retry-After</c> header, matching the rest of the error surface.
///   - Window sizes are generous enough for normal client code (SDK
///     retries, batched uploads) but tight enough to blunt abuse.
/// </summary>
public static class RateLimiting
{
    public const string AppsPolicy = "apps";
    public const string ProblemsPolicy = "problems";
    public const string AuthPolicy = "auth";
    public const string AuthLoginPolicy = "auth-login";
    public const string StatusPolicy = "status";
    public const string SubscribePolicy = "subscribe";

    private const string BearerPrefix = "Bearer ";
    private const string AnonymousPartition = "__anon__";

    public static IServiceCollection AddBumpRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // On rejection, emit problem+json so the shape matches other
            // 4xx responses. Also surface the window via Retry-After when
            // the limiter tells us how long to wait.
            options.OnRejected = async (context, cancellationToken) =>
            {
                TimeSpan? retryAfter = null;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after))
                {
                    retryAfter = after;
                    context.HttpContext.Response.Headers[HeaderNames.RetryAfter] =
                        ((int)Math.Ceiling(after.TotalSeconds)).ToString();
                }

                var result = JsonResults.TooManyRequests(
                    title: "Too many requests",
                    detail: "The rate limit for your API key has been exceeded. Retry after the interval indicated in the Retry-After header.",
                    retryAfter: retryAfter);
                await result.ExecuteAsync(context.HttpContext);
            };

            // /api/apps — mutations + reads. Generous, since inventory
            // queries from CI pipelines tend to burst.
            options.AddPolicy(AppsPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/problems — noisy by nature (every unhandled exception
            // posts one). Still, a runaway loop can saturate ingestion,
            // so cap per-key throughput.
            options.AddPolicy(ProblemsPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/auth/* — small bucket per IP to blunt brute-force.
            options.AddPolicy(AuthPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/auth/login — even tighter cap to blunt credential
            // stuffing. Per-account lockout in AuthController complements
            // this for accounts the attacker has actually targeted.
            options.AddPolicy(AuthLoginPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/subscribers — unauthenticated sign-up. Tight per-IP
            // cap to keep the endpoint from being used as a mailbomb
            // relay; CAPTCHA in the controller is the primary defense
            // and a per-board cap is the last line.
            options.AddPolicy(SubscribePolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/status — public anonymous endpoints. Cap per IP to
            // protect from runaway pollers.
            options.AddPolicy(StatusPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    /// <summary>
    /// Partition by the raw bearer token. The token is the stable,
    /// per-caller identifier we already authenticate on, so using it
    /// directly keeps the partition aligned with the auth surface.
    /// </summary>
    private static string GetPartitionKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var header))
        {
            var value = header.ToString();
            if (value.StartsWith(BearerPrefix, StringComparison.Ordinal))
            {
                var token = value[BearerPrefix.Length..].Trim();
                if (token.Length > 0)
                {
                    return token;
                }
            }
        }
        return AnonymousPartition;
    }
}
