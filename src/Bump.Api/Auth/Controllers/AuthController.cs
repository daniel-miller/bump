using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Bump.Api.Auth.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
[EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class AuthController : ControllerBase
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AppUserRepository _users;
    private readonly UserSessionRepository _sessions;
    private readonly UserRecoveryCodeRepository _recovery;
    private readonly PasswordResetTokenRepository _resetTokens;
    private readonly JwtIssuer _jwt;
    private readonly BumpCookieOptions _cookies;
    private readonly IMailgunClient _mail;
    private readonly IConfiguration _config;

    public AuthController(
        AppUserRepository users,
        UserSessionRepository sessions,
        UserRecoveryCodeRepository recovery,
        PasswordResetTokenRepository resetTokens,
        JwtIssuer jwt,
        BumpCookieOptions cookies,
        IMailgunClient mail,
        IConfiguration config)
    {
        _users = users;
        _sessions = sessions;
        _recovery = recovery;
        _resetTokens = resetTokens;
        _jwt = jwt;
        _cookies = cookies;
        _mail = mail;
        _config = config;
    }

    public sealed record LoginRequest(string Email, string Password, string? Totp);

    /// <summary>Establish an authenticated session. Sets <c>bump_session</c> and <c>bump_csrf</c> cookies on success.</summary>
    /// <remarks>If TOTP is enabled on the account, <c>totp</c> must be supplied. After 5 failures the account is locked for 15 minutes.</remarks>
    [HttpPost("login", Name = "login")]
    [EnableRateLimiting(RateLimiting.AuthLoginPolicy)]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        {
            return JsonResults.UnprocessableEntity("Invalid credentials", "Email and password are required.").AsAction();
        }

        var user = await _users.GetByEmailAsync(req.Email, ct);
        if (user is null)
        {
            // Run a hash to keep the timing roughly constant whether or not
            // the email exists; do not leak account presence.
            _ = PasswordHasher.Verify(req.Password, new byte[32], new byte[16]);
            return JsonResults.Unauthorized("Invalid email or password.").AsAction();
        }

        if (user.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            return JsonResults.Unauthorized("Invalid email or password.").AsAction();
        }

        if (!PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
        {
            await _users.RecordFailedLoginAsync(user.UserId, MaxFailedLogins, LockoutDuration, ct);
            return JsonResults.Unauthorized("Invalid email or password.").AsAction();
        }

        if (user.TotpEnabled)
        {
            if (string.IsNullOrWhiteSpace(req.Totp))
            {
                return JsonResults.Problem(
                    StatusCodes.Status401Unauthorized,
                    title: "Two-factor required",
                    detail: "Provide a TOTP code in the totp field.").AsAction();
            }
            var ok = TotpService.Verify(user.TotpSecret!, req.Totp);
            if (!ok)
            {
                var recoveryHash = TotpService.HashCode(req.Totp);
                ok = await _recovery.ConsumeAsync(user.UserId, recoveryHash, ct);
            }
            if (!ok)
            {
                await _users.RecordFailedLoginAsync(user.UserId, MaxFailedLogins, LockoutDuration, ct);
                return JsonResults.Unauthorized("Invalid TOTP code.").AsAction();
            }
        }

        await _users.ResetFailedLoginAsync(user.UserId, ct);
        await IssueSessionAsync(user.UserId, ct);
        return NoContent();
    }

    /// <summary>Revoke the current session and clear auth cookies.</summary>
    [HttpPost("logout", Name = "logout")]
    [Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Guid.TryParse(User.FindFirstValue("sid"), out var sid))
        {
            await _sessions.RevokeAsync(sid, ct);
        }
        ClearAuthCookies();
        return NoContent();
    }

    public sealed record PasswordResetRequest(string Email);

    /// <summary>Begin a password reset by emailing a one-hour token. Always returns 202 to prevent account enumeration.</summary>
    [HttpPost("password-resets", Name = "requestPasswordReset")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> PasswordResetReq([FromBody] PasswordResetRequest req, CancellationToken ct)
    {
        // Always 202 to avoid leaking which emails exist.
        var user = await _users.GetByEmailAsync(req.Email, ct);
        if (user is not null)
        {
            var raw = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expires = DateTimeOffset.UtcNow.AddHours(1);
            await _resetTokens.CreateAsync(user.UserId, hash, expires, ct);

            var publicUrl = _config["Bump:Hosting:PublicBaseUrl"] ?? "https://status.bump.example.com";
            var resetUrl = $"{publicUrl.TrimEnd('/')}/reset-password?token={token}";
            await _mail.SendAsync(PasswordReset.Build(user.UserEmail, resetUrl), ct);
        }
        return Accepted();
    }

    public sealed record PasswordResetConfirmRequest(string Token, string NewPassword);

    /// <summary>Complete a password reset using the emailed token. Revokes all existing sessions for the user.</summary>
    [HttpPost("password-resets/confirm", Name = "confirmPasswordReset")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> PasswordResetConfirm([FromBody] PasswordResetConfirmRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
        {
            return JsonResults.UnprocessableEntity("Invalid request", "Token and newPassword are required.").AsAction();
        }
        if (req.NewPassword.Length < 12)
        {
            return JsonResults.UnprocessableEntity("Password too short", "Password must be at least 12 characters.").AsAction();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(req.Token));
        var record = await _resetTokens.ConsumeAsync(hash, ct);
        if (record is null)
        {
            return JsonResults.UnprocessableEntity("Token invalid", "Token expired, used, or unknown.").AsAction();
        }

        var (pwHash, pwSalt) = PasswordHasher.Hash(req.NewPassword);
        await _users.UpdatePasswordAsync(record.UserId, pwHash, pwSalt, ct);
        await _sessions.RevokeAllForUserAsync(record.UserId, ct);
        return NoContent();
    }

    private async Task IssueSessionAsync(Guid userId, CancellationToken ct)
    {
        var (jwt, sessionId, expires) = _jwt.Issue(userId);
        var hash = JwtIssuer.HashSessionId(sessionId);
        var ua = HttpContext.Request.Headers.UserAgent.ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _sessions.CreateAsync(sessionId, userId, hash, expires, ua, ip, ct);

        var sessionCookie = BuildCookieOptions(expires);
        sessionCookie.HttpOnly = true;
        Response.Cookies.Append(_cookies.SessionCookieName, jwt, sessionCookie);

        var csrfRaw = RandomNumberGenerator.GetBytes(24);
        var csrf = Convert.ToBase64String(csrfRaw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var csrfCookie = BuildCookieOptions(expires);
        csrfCookie.HttpOnly = false;
        Response.Cookies.Append(_cookies.CsrfCookieName, csrf, csrfCookie);
    }

    private CookieOptions BuildCookieOptions(DateTimeOffset expires)
    {
        var options = new CookieOptions
        {
            Path = "/",
            Secure = _cookies.Secure,
            SameSite = _cookies.SameSiteMode,
            Expires = expires,
            IsEssential = true
        };
        if (!string.IsNullOrWhiteSpace(_cookies.Domain))
        {
            options.Domain = _cookies.Domain;
        }
        return options;
    }

    private void ClearAuthCookies()
    {
        var dead = new CookieOptions
        {
            Path = "/",
            Secure = _cookies.Secure,
            SameSite = _cookies.SameSiteMode,
            Expires = DateTimeOffset.UnixEpoch,
            Domain = _cookies.Domain
        };
        Response.Cookies.Delete(_cookies.SessionCookieName, dead);
        Response.Cookies.Delete(_cookies.CsrfCookieName, dead);
    }
}
