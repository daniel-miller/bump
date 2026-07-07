using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bump.Api.Auth;

/// <summary>
/// Double-submit CSRF check. Compares the value of the non-HttpOnly
/// <c>bump_csrf</c> cookie against an <c>X-Bump-Csrf</c> request header.
/// Mismatch → 403.
///
/// Registered as a global authorization filter in
/// <see cref="Program"/>. Safe methods (GET/HEAD/OPTIONS) and requests
/// without the session cookie pass through. Controllers authenticated
/// only by a Bearer key (Apps, Problems) opt out via
/// <see cref="BypassCsrfAttribute"/> so the filter never inspects them.
/// </summary>
public sealed class CsrfFilter : IAsyncAuthorizationFilter
{
    private const string HeaderName = "X-Bump-Csrf";

    private readonly BumpCookieOptions _cookies;

    public CsrfFilter(BumpCookieOptions cookies) => _cookies = cookies;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Explicit opt-out for Bearer-key surfaces.
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is BypassCsrfAttribute))
        {
            return Task.CompletedTask;
        }

        var request = context.HttpContext.Request;
        var method = request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return Task.CompletedTask;
        }

        // Only enforce when the call is presenting a session cookie.
        if (!request.Cookies.TryGetValue(_cookies.SessionCookieName, out _))
        {
            return Task.CompletedTask;
        }

        var cookieToken = request.Cookies[_cookies.CsrfCookieName];
        var headerToken = request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken) || cookieToken != headerToken)
        {
            context.Result = JsonResults.Problem(
                StatusCodes.Status403Forbidden,
                title: "CSRF token missing or invalid",
                detail: $"Request must include matching {_cookies.CsrfCookieName} cookie and {HeaderName} header.").AsAction();
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Marker attribute that exempts a controller or action from
/// <see cref="CsrfFilter"/>. Apply only to surfaces authenticated by
/// something other than the session cookie (e.g. Bearer keys).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class BypassCsrfAttribute : Attribute { }
