using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bump.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Bump.Api;

/// <summary>
/// MVC authorization filter that guards <c>/api/apps</c>.
///
/// Two paths are accepted:
///   1. Bearer key in the <c>Authorization</c> header (from app SDKs and CLIs).
///      Keys come from <c>Bump:Api:Security:Apps:ClientSecrets</c>. Always allowed.
///   2. Session cookie with the <c>admin</c> role, but only on safe methods
///      (GET/HEAD). Lets the React admin UI list/inspect apps without holding
///      a Bearer key.
///
/// Bearer comparisons run <see cref="CryptographicOperations.FixedTimeEquals"/>
/// against every configured key so timing cannot leak which key matched.
/// </summary>
public sealed class ApiKeyAuthFilter : IAsyncAuthorizationFilter
{
    private const string BearerPrefix = "Bearer ";

    private readonly byte[][] _keys;
    private readonly JwtIssuer _jwt;
    private readonly UserSessionRepository _sessions;
    private readonly AppUserRepository _users;
    private readonly BumpCookieOptions _cookies;
    private readonly ILogger<ApiKeyAuthFilter> _logger;

    public ApiKeyAuthFilter(
        IConfiguration config,
        JwtIssuer jwt,
        UserSessionRepository sessions,
        AppUserRepository users,
        BumpCookieOptions cookies,
        ILogger<ApiKeyAuthFilter> logger)
    {
        _logger = logger;
        _jwt = jwt;
        _sessions = sessions;
        _users = users;
        _cookies = cookies;

        var configured = config.GetSection("Bump:Api:Security:Apps:ClientSecrets").Get<string[]>() ?? Array.Empty<string>();
        _keys = configured
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Encoding.UTF8.GetBytes(k))
            .ToArray();

        if (_keys.Length == 0)
        {
            _logger.LogWarning("No API keys configured. /api/apps will only accept admin session cookies for safe methods.");
        }
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;

        // 1) Bearer key path.
        if (request.Headers.TryGetValue(HeaderNames.Authorization, out var header))
        {
            var value = header.ToString();
            if (!value.StartsWith(BearerPrefix, StringComparison.Ordinal))
            {
                context.Result = JsonResults.Unauthorized("Authorization header must use the Bearer scheme.").AsAction();
                return;
            }

            var token = value[BearerPrefix.Length..].Trim();
            var tokenBytes = Encoding.UTF8.GetBytes(token);

            var matched = 0;
            foreach (var keyBytes in _keys)
            {
                matched |= CryptographicOperations.FixedTimeEquals(tokenBytes, keyBytes) ? 1 : 0;
            }

            if (matched == 0)
            {
                context.Result = JsonResults.Unauthorized("Invalid API key.").AsAction();
            }
            return;
        }

        // 2) Session cookie path — admin role only, safe methods only.
        var method = request.Method;
        var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        if (isSafe && request.Cookies.TryGetValue(_cookies.SessionCookieName, out var jwt) && !string.IsNullOrEmpty(jwt))
        {
            if (_jwt.TryValidate(jwt, out var userId, out var sessionId))
            {
                var session = await _sessions.GetActiveAsync(sessionId, context.HttpContext.RequestAborted);
                if (session is not null && session.UserId == userId)
                {
                    var user = await _users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                    if (user is null || !user.Roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
                    {
                        context.Result = JsonResults.Unauthorized("Admin role required.").AsAction();
                        return;
                    }
                    var identity = new ClaimsIdentity(SessionAuthHandler.SchemeName);
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
                    identity.AddClaim(new Claim("sid", sessionId.ToString()));
                    foreach (var role in user.Roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
                    context.HttpContext.User = new ClaimsPrincipal(identity);
                    return;
                }
            }
            context.Result = JsonResults.Unauthorized("Session expired or invalid.").AsAction();
            return;
        }

        context.Result = JsonResults.Unauthorized("Authentication required.").AsAction();
    }
}

/// <summary>Attribute placement for <see cref="ApiKeyAuthFilter"/> on apps controllers.</summary>
public sealed class ApiKeyAuthorizeAttribute : ServiceFilterAttribute
{
    public ApiKeyAuthorizeAttribute() : base(typeof(ApiKeyAuthFilter)) { }
}
