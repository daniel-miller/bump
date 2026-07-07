using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Bump.Api;

/// <summary>
/// MVC resource filter implementing the Stripe-style <c>Idempotency-Key</c>
/// pattern. Sending the header is optional — when absent, the filter is a
/// no-op and requests proceed normally.
///
/// When the header is present:
///   - If a cached response exists for this (api key, idempotency key) and
///     the request body fingerprint matches, the cached response is
///     replayed and the action is not re-run.
///   - If a cached response exists but the body fingerprint differs, the
///     request is rejected with 422 — a clear signal that the client
///     reused a key for something semantically different.
///   - If no cached response exists, the action runs and the first
///     successful (2xx) response is stored for future replay.
///
/// Partitioning is by the bearer token's SHA-256 hash, never the raw
/// token, so the idempotency table never sees the secret.
///
/// Runs as <see cref="IAsyncResourceFilter"/> so it can buffer the request
/// body before model binding consumes it.
/// </summary>
public sealed class IdempotencyFilter : IAsyncResourceFilter
{
    private const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 255;

    private readonly IdempotencyRepository _repo;

    public IdempotencyFilter(IdempotencyRepository repo)
    {
        _repo = repo;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        if (!request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            await next();
            return;
        }

        var key = headerValue.ToString().Trim();
        if (key.Length == 0 || key.Length > MaxKeyLength)
        {
            context.Result = JsonResults.BadRequest(
                title: "Invalid Idempotency-Key",
                detail: $"Idempotency-Key must be a non-empty string no longer than {MaxKeyLength} characters.").AsAction();
            return;
        }

        // Require an Authorization header so partitioning has something
        // to hash. The auth filter would reject an unauth'd request anyway,
        // but without a key we can't safely cache a response against any
        // caller identity.
        if (!request.Headers.TryGetValue(HeaderNames.Authorization, out var auth))
        {
            await next();
            return;
        }
        var apiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(auth.ToString()));

        // Buffer the body so we can hash it AND so model binding can still
        // read it.
        request.EnableBuffering();
        var bodyBytes = await ReadFullyAsync(request.Body, httpContext.RequestAborted);
        request.Body.Position = 0;

        // Scope the fingerprint to (method, path, body) so the same
        // Idempotency-Key reused on a different endpoint cannot replay
        // an unrelated cached response. Matches Stripe's per-endpoint scope.
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n"));
        hasher.AppendData(bodyBytes);
        var fingerprint = hasher.GetHashAndReset();

        // Lookup
        var existing = await _repo.GetAsync(apiKeyHash, key);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(existing.RequestFingerprint, fingerprint))
            {
                context.Result = JsonResults.UnprocessableEntity(
                    title: "Idempotency-Key reuse",
                    detail: "The same Idempotency-Key was used with a different request body. Use a fresh key or the original body.").AsAction();
                return;
            }
            context.Result = new CachedResponseActionResult(existing);
            return;
        }

        // Not cached — run the action, capturing its response bytes by
        // swapping the response body stream so the first 2xx outcome can be
        // stored for future replay.
        var originalBody = httpContext.Response.Body;
        using var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            httpContext.Response.Body = originalBody;
        }

        var status = httpContext.Response.StatusCode;
        var contentType = httpContext.Response.ContentType;
        var responseBytes = buffer.ToArray();

        await originalBody.WriteAsync(responseBytes);

        if (status >= 200 && status < 300)
        {
            await _repo.StoreAsync(apiKeyHash, key, fingerprint, status, contentType, responseBytes);
        }
    }

    private static async Task<byte[]> ReadFullyAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var rent = ArrayPool<byte>.Shared.Rent(8 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(rent.AsMemory(0, rent.Length), ct)) > 0)
            {
                ms.Write(rent, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent);
        }
        return ms.ToArray();
    }
}

/// <summary>
/// Marker + filter placement for <see cref="IdempotencyFilter"/>. Apply to
/// every POST action that should honor <c>Idempotency-Key</c>; the same
/// attribute also tells the OpenAPI generator to document the header.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : ServiceFilterAttribute
{
    public IdempotentAttribute() : base(typeof(IdempotencyFilter)) { }
}

/// <summary>
/// Replay of a stored response from the idempotency cache. Writes the
/// original status, content-type, and bytes back verbatim.
/// </summary>
internal sealed class CachedResponseActionResult : IActionResult
{
    private readonly IdempotencyRecord _record;

    public CachedResponseActionResult(IdempotencyRecord record)
    {
        _record = record;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = _record.ResponseStatus;
        if (_record.ResponseContentType is not null)
        {
            response.ContentType = _record.ResponseContentType;
        }
        response.Headers["Idempotent-Replayed"] = "true";
        await response.Body.WriteAsync(_record.ResponseBody);
    }
}
