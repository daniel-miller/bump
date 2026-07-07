using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Bump.Api.Auth;

public sealed class SessionAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Session";

    private readonly JwtIssuer _jwt;
    private readonly UserSessionRepository _sessions;
    private readonly AppUserRepository _users;
    private readonly BumpCookieOptions _cookies;

    public SessionAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        JwtIssuer jwt,
        UserSessionRepository sessions,
        AppUserRepository users,
        BumpCookieOptions cookies)
        : base(options, loggerFactory, encoder)
    {
        _jwt = jwt;
        _sessions = sessions;
        _users = users;
        _cookies = cookies;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Cookies.TryGetValue(_cookies.SessionCookieName, out var cookie) ||
            string.IsNullOrEmpty(cookie))
        {
            return AuthenticateResult.NoResult();
        }

        if (!_jwt.TryValidate(cookie, out var userId, out var sessionId))
        {
            return AuthenticateResult.Fail("Invalid session token.");
        }

        var session = await _sessions.GetActiveAsync(sessionId, Context.RequestAborted);
        if (session is null || session.UserId != userId)
        {
            return AuthenticateResult.Fail("Session revoked or expired.");
        }

        var user = await _users.GetByIdAsync(userId, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("User no longer exists.");
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        identity.AddClaim(new Claim("sid", sessionId.ToString()));
        identity.AddClaim(new Claim("tz", user.UserTimezone));
        foreach (var role in user.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }
}
