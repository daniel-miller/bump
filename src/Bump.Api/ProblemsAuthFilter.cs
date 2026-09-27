using System.Security.Cryptography;
using System.Text;
using Bump.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Bump.Api;

/// <summary>
/// MVC authorization filter that guards <c>/api/problems</c>. Three credentials are accepted,
/// each for a different job:
///   1. The reporter key, <c>Bump:Api:Hosting:ClientSecret</c>. Every Bump.Sdk consumer holds it,
///      so it may only report: <c>POST /api/problems</c> and nothing else.
///   2. A read key, any entry of <c>Bump:Api:Problems:ReadSecrets</c>. Safe methods only, so an
///      unattended reader (a monitor, an agent) can list and fetch problems but never resolve or
///      delete them.
///   3. A session cookie (the React admin UI), which may do everything.
/// A key that is valid but used outside its job answers 403, not 401, so the caller can tell a
/// wrong key from a key doing the wrong thing. Comparisons are fixed-time.
/// </summary>
public sealed class ProblemsAuthFilter : IAsyncAuthorizationFilter
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>The one action the reporter key may call.</summary>
    public const string ReportActionName = "createProblem";

    public enum KeyRole { None, Reporter, Reader }

    private readonly byte[] _reporterKey;
    private readonly byte[][] _readKeys;
    private readonly JwtIssuer _jwt;
    private readonly UserSessionRepository _sessions;
    private readonly BumpCookieOptions _cookies;

    public ProblemsAuthFilter(
        IConfiguration config,
        JwtIssuer jwt,
        UserSessionRepository sessions,
        BumpCookieOptions cookies,
        ILogger<ProblemsAuthFilter> logger)
    {
        _jwt = jwt;
        _sessions = sessions;
        _cookies = cookies;
        _reporterKey = Encoding.UTF8.GetBytes(config["Bump:Api:Hosting:ClientSecret"] ?? "");
        _readKeys = (config.GetSection("Bump:Api:Problems:ReadSecrets").Get<string[]>() ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Encoding.UTF8.GetBytes(k))
            .ToArray();

        if (_reporterKey.Length == 0)
        {
            logger.LogWarning("Bump:Api:Hosting:ClientSecret is not configured. /api/problems will require a session cookie.");
        }
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;

        // 1) Bearer key path: Bump.Sdk reporters and read-only consumers.
        if (request.Headers.TryGetValue(HeaderNames.Authorization, out var header))
        {
            var value = header.ToString();
            if (value.StartsWith(BearerPrefix, StringComparison.Ordinal) && (_reporterKey.Length > 0 || _readKeys.Length > 0))
            {
                var token = Encoding.UTF8.GetBytes(value[BearerPrefix.Length..].Trim());
                var role = Match(token, _reporterKey, _readKeys);
                if (role == KeyRole.None)
                {
                    context.Result = JsonResults.Unauthorized("Invalid API key.").AsAction();
                    return;
                }

                var actionName = (context.ActionDescriptor as ControllerActionDescriptor)?.AttributeRouteInfo?.Name
                    ?? context.ActionDescriptor.AttributeRouteInfo?.Name;
                if (!Permits(role, request.Method, actionName))
                {
                    context.Result = JsonResults.Forbidden(role == KeyRole.Reporter
                        ? "The reporter key can only report problems (POST /api/problems). Use a read key to query them."
                        : "A read key can only list and fetch problems. Reporting uses the reporter key; resolving and deleting need an admin session.")
                        .AsAction();
                }
                return;
            }
        }

        // 2) Session cookie path (used by the React UI).
        if (request.Cookies.TryGetValue(_cookies.SessionCookieName, out var jwt) && !string.IsNullOrEmpty(jwt))
        {
            if (_jwt.TryValidate(jwt, out var userId, out var sessionId))
            {
                var session = await _sessions.GetActiveAsync(sessionId, context.HttpContext.RequestAborted);
                if (session is not null && session.UserId == userId)
                {
                    return;
                }
            }
            context.Result = JsonResults.Unauthorized("Session expired or invalid.").AsAction();
            return;
        }

        context.Result = JsonResults.Unauthorized("Authentication required.").AsAction();
    }

    /// <summary>
    /// Which key a token is. Every configured key is compared, with no early exit, so timing does
    /// not reveal which one matched. Startup refuses a read key equal to the reporter key, so a
    /// token can match at most one role.
    /// </summary>
    public static KeyRole Match(byte[] token, byte[] reporterKey, byte[][] readKeys)
    {
        var reporter = reporterKey.Length > 0 && CryptographicOperations.FixedTimeEquals(token, reporterKey);
        var reader = false;
        foreach (var key in readKeys)
        {
            reader |= CryptographicOperations.FixedTimeEquals(token, key);
        }

        return reporter ? KeyRole.Reporter : reader ? KeyRole.Reader : KeyRole.None;
    }

    public static bool Permits(KeyRole role, string method, string? actionName) => role switch
    {
        KeyRole.Reporter => HttpMethods.IsPost(method) && actionName == ReportActionName,
        KeyRole.Reader => HttpMethods.IsGet(method) || HttpMethods.IsHead(method),
        _ => false,
    };
}

/// <summary>Attribute placement for <see cref="ProblemsAuthFilter"/> on problems controllers.</summary>
public sealed class ProblemsAuthorizeAttribute : ServiceFilterAttribute
{
    public ProblemsAuthorizeAttribute() : base(typeof(ProblemsAuthFilter)) { }
}
