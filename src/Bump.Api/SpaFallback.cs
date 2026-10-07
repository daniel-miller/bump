using Bump.Api.Services;

namespace Bump.Api;

/// <summary>
/// Serves the SPA shell for client routes with an honest HTTP status.
///
/// A blanket <c>MapFallbackToFile("index.html")</c> answers every unmatched
/// path with a 200, so a URL the router renders as "404 Not Found" still
/// reads as a live page to a crawler (a soft 404). This fallback knows the
/// client route table: a known route gets the shell with a 200, anything
/// else gets the same shell with a 404 so the SPA still renders its own
/// not-found view. A public board for a handle no owner holds is a 404 too.
/// </summary>
public static class SpaFallback
{
    /// <summary>
    /// Mirrors the <c>path</c> entries in <c>web/src/router.tsx</c>.
    /// <c>SpaFallbackTests</c> fails the build when the two drift apart.
    /// </summary>
    public static readonly string[] Routes =
    [
        "/",
        "/login",
        "/login/mfa",
        "/boards/:handle",
        "/subscribe/confirm",
        "/unsubscribe",
        "/account/confirm-email",
        "/dashboard",
        "/services",
        "/services/:handle",
        "/outages",
        "/outages/:id",
        "/owners",
        "/owners/:handle",
        "/announcements",
        "/apps",
        "/environments",
        "/servers",
        "/problems",
        "/problems/:id",
        "/account",
        "/security",
        "/about",
    ];

    private static readonly string[][] RouteSegments = Routes.Select(Segments).ToArray();

    /// <summary>
    /// True when the path matches a client route. Matching is
    /// case-insensitive and ignores a trailing slash, the same as React
    /// Router's defaults.
    /// </summary>
    public static bool IsClientRoute(string path)
    {
        var segments = Segments(path);
        return RouteSegments.Any(route => Matches(route, segments));
    }

    /// <summary>
    /// The handle in a <c>/boards/:handle</c> path, or null for any other path.
    /// </summary>
    public static string? BoardHandle(string path)
    {
        var segments = Segments(path);
        return segments.Length == 2 && segments[0].Equals("boards", StringComparison.OrdinalIgnoreCase)
            ? segments[1]
            : null;
    }

    public static async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var status = StatusCodes.Status200OK;

        if (!IsClientRoute(path))
        {
            status = StatusCodes.Status404NotFound;
        }
        else if (BoardHandle(path) is { } handle)
        {
            var owners = context.RequestServices.GetRequiredService<OwnerRepository>();
            if (await owners.GetByHandleAsync(handle, context.RequestAborted) is null)
                status = StatusCodes.Status404NotFound;
        }

        // Local dev serves the SPA from the Vite dev server, so wwwroot can
        // be empty. Without a shell there is nothing to render but the status.
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var shell = env.WebRootFileProvider.GetFileInfo("index.html");
        context.Response.StatusCode = status;
        if (!shell.Exists) return;

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(shell, context.RequestAborted);
    }

    private static string[] Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string[] route, string[] segments) =>
        route.Length == segments.Length
        && route.Zip(segments).All(pair =>
            pair.First.StartsWith(':')
            || pair.First.Equals(pair.Second, StringComparison.OrdinalIgnoreCase));
}
