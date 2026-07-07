using System.Security.Cryptography;
using System.Text;
using Bump.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Bump.Api;

/// <summary>
/// MVC authorization filter that guards <c>/api/problems</c> with a single preshared key
/// supplied in the <c>Authorization</c> header as <c>Bearer &lt;key&gt;</c>.
/// The key is read from configuration at <c>Bump:Security:Problems:ApiKey</c>.
/// Uses a fixed-time comparison so a timing side-channel cannot leak the key.
/// </summary>
public sealed class ProblemsAuthFilter : IAsyncAuthorizationFilter
{
    private const string BearerPrefix = "Bearer ";

    private readonly byte[] _keyBytes;
    private readonly JwtIssuer _jwt;
    private readonly UserSessionRepository _sessions;
    private readonly BumpCookieOptions _cookies;
    private readonly ILogger<ProblemsAuthFilter> _logger;

    public ProblemsAuthFilter(
        IConfiguration config,
        JwtIssuer jwt,
        UserSessionRepository sessions,
        BumpCookieOptions cookies,
        ILogger<ProblemsAuthFilter> logger)
    {
        _logger = logger;
        _jwt = jwt;
        _sessions = sessions;
        _cookies = cookies;
        var key = config["Bump:Security:Problems:ApiKey"] ?? "";
        _keyBytes = Encoding.UTF8.GetBytes(key);

        if (_keyBytes.Length == 0)
        {
            _logger.LogWarning("Bump:Security:Problems:ApiKey is not configured. /api/problems will require a session cookie.");
        }
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;

        // 1) Bearer key path (used by Bump.Sdk).
        if (request.Headers.TryGetValue(HeaderNames.Authorization, out var header))
        {
            var value = header.ToString();
            if (value.StartsWith(BearerPrefix, StringComparison.Ordinal) && _keyBytes.Length > 0)
            {
                var token = value[BearerPrefix.Length..].Trim();
                var tokenBytes = Encoding.UTF8.GetBytes(token);
                if (CryptographicOperations.FixedTimeEquals(tokenBytes, _keyBytes))
                {
                    return;
                }
                context.Result = JsonResults.Unauthorized("Invalid API key.").AsAction();
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
}

/// <summary>Attribute placement for <see cref="ProblemsAuthFilter"/> on problems controllers.</summary>
public sealed class ProblemsAuthorizeAttribute : ServiceFilterAttribute
{
    public ProblemsAuthorizeAttribute() : base(typeof(ProblemsAuthFilter)) { }
}
