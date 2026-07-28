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

    public static IServiceCollection AddBumpRateLimiting(this IServiceCollection services, RateLimitSettings limits)
    {
        // A limiter built with a non-positive permit or window throws deep inside the
        // rate-limiter factory on the first matching request, not at startup - so the app
        // would look healthy and then 500 the endpoint the setting was meant to protect.
        Validate(nameof(limits.Apps), limits.Apps);
        Validate(nameof(limits.Problems), limits.Problems);
        Validate(nameof(limits.Auth), limits.Auth);
        Validate(nameof(limits.AuthLogin), limits.AuthLogin);
        Validate(nameof(limits.Subscribe), limits.Subscribe);
        Validate(nameof(limits.Status), limits.Status);

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
                        PermitLimit = limits.Apps.PermitLimit,
                        Window = limits.Apps.Window,
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
                        PermitLimit = limits.Problems.PermitLimit,
                        Window = limits.Problems.Window,
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
                        PermitLimit = limits.Auth.PermitLimit,
                        Window = limits.Auth.Window,
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
                        PermitLimit = limits.AuthLogin.PermitLimit,
                        Window = limits.AuthLogin.Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            // /api/subscribers — unauthenticated sign-up. Tight per-IP
            // cap to keep the endpoint from being used as a mailbomb
            // relay. With no CAPTCHA on the endpoint this limiter is the
            // primary defense; a per-board cap is the last line.
            options.AddPolicy(SubscribePolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.Subscribe.PermitLimit,
                        Window = limits.Subscribe.Window,
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
                        PermitLimit = limits.Status.PermitLimit,
                        Window = limits.Status.Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static void Validate(string name, RateLimitPolicySettings policy)
    {
        if (policy.PermitLimit <= 0 || policy.WindowMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Bump:Api:RateLimits:{name} is invalid (PermitLimit={policy.PermitLimit}, "
                + $"WindowMinutes={policy.WindowMinutes}). Both must be greater than zero. "
                + "To widen a limit, raise PermitLimit; there is no 'unlimited' value.");
        }
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
