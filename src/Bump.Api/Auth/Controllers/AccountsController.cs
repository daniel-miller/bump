using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Auth.Controllers;

[ApiController]
[Route("api/accounts")]
[Tags("Accounts")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName)]
public sealed class AccountsController : ControllerBase
{
    private readonly AppUserRepository _users;
    private readonly UserSessionRepository _sessions;
    private readonly EmailChangeTokenRepository _emailChange;
    private readonly UserRecoveryCodeRepository _recovery;
    private readonly IMailgunClient _mail;
    private readonly IConfiguration _config;

    public AccountsController(
        AppUserRepository users,
        UserSessionRepository sessions,
        EmailChangeTokenRepository emailChange,
        UserRecoveryCodeRepository recovery,
        IMailgunClient mail,
        IConfiguration config)
    {
        _users = users;
        _sessions = sessions;
        _emailChange = emailChange;
        _recovery = recovery;
        _mail = mail;
        _config = config;
    }

    public sealed record ProfileResponse(Guid UserId, string Email, string FullName, string Timezone, bool TotpEnabled, string? IpAddress);

    /// <summary>Return the profile of the signed-in account along with the request's source IP.</summary>
    [HttpGet("me", Name = "getCurrentAccount")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var id = CurrentId();
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Unauthorized();

        // Prefer X-Forwarded-For when behind a proxy (Railway etc.); fall back to RemoteIpAddress.
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            ip = ip.Split(',')[0].Trim();
        }
        else
        {
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        return Ok(new ProfileResponse(user.UserId, user.UserEmail, user.UserFullName, user.UserTimezone, user.TotpEnabled, ip));
    }

    public sealed record UpdateProfileRequest(string FullName, string Timezone);

    /// <summary>Update the signed-in account's display name and time zone.</summary>
    /// <remarks>Time zone must be a recognized IANA (e.g. <c>America/Denver</c>) or Windows identifier.</remarks>
    [HttpPatch("me", Name = "updateCurrentAccount")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Timezone))
        {
            return JsonResults.UnprocessableEntity("Invalid profile", "FullName and Timezone are required.").AsAction();
        }
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return JsonResults.UnprocessableEntity("Invalid timezone", $"'{req.Timezone}' is not a recognized IANA or Windows time zone identifier (e.g. 'America/Denver').").AsAction();
        }
        catch (InvalidTimeZoneException)
        {
            return JsonResults.UnprocessableEntity("Invalid timezone", $"'{req.Timezone}' is corrupted and cannot be loaded.").AsAction();
        }
        await _users.UpdateProfileAsync(CurrentId(), req.FullName, req.Timezone, ct);
        return NoContent();
    }

    /// <summary>Send a test email to the signed-in account's address to verify outbound email delivery.</summary>
    [HttpPost("me/test-email", Name = "sendTestEmail")]
    public async Task<IActionResult> SendTestEmail(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(CurrentId(), ct);
        if (user is null) return Unauthorized();
        await _mail.SendAsync(TestEmail.Build(user.UserEmail), ct);
        return Accepted();
    }

    public sealed record StartEmailChangeRequest(string Password, string NewEmail);

    /// <summary>Begin changing the signed-in account's email. Re-auth via password; emails a confirmation link to the new address.</summary>
    [HttpPost("me/email-changes", Name = "startEmailChange")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> StartEmailChange([FromBody] StartEmailChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.NewEmail))
        {
            return JsonResults.UnprocessableEntity("Invalid request", "Password and NewEmail are required.").AsAction();
        }
        if (!IsValidEmail(req.NewEmail))
        {
            return JsonResults.UnprocessableEntity("Invalid email", "NewEmail is not a valid address.").AsAction();
        }

        var user = await _users.GetByIdAsync(CurrentId(), ct);
        if (user is null) return Unauthorized();
        if (!PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
        {
            return JsonResults.Unauthorized("Password did not match.").AsAction();
        }
        if (string.Equals(user.UserEmail, req.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            return JsonResults.UnprocessableEntity("No change", "NewEmail matches the current address.").AsAction();
        }

        // High-entropy token: raw value goes in the email link, only its
        // SHA-256 lives in the database.
        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        await _emailChange.CreateAsync(user.UserId, req.NewEmail, hash, expires, ct);

        var publicUrl = (_config["Bump:Hosting:PublicBaseUrl"] ?? "").TrimEnd('/');
        var confirmUrl = $"{publicUrl}/account/confirm-email?token={token}";
        await _mail.SendAsync(EmailChangeConfirm.Build(req.NewEmail, confirmUrl), ct);

        return Accepted();
    }

    public sealed record ConfirmEmailChangeRequest(string Token);

    /// <summary>Complete an email change using the emailed token. Requires the same session that initiated the change.</summary>
    [HttpPost("me/email-changes/confirm", Name = "confirmEmailChange")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
        {
            return JsonResults.UnprocessableEntity("Invalid request", "Token is required.").AsAction();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(req.Token));
        var record = await _emailChange.ConsumeAsync(hash, ct);
        if (record is null)
        {
            return JsonResults.UnprocessableEntity("Token invalid", "Token expired, used, or unknown.").AsAction();
        }
        // The session-bound user must match the owner of the token. This
        // prevents an attacker from confirming someone else's pending
        // change by relaying a stolen link.
        if (record.UserId != CurrentId())
        {
            return JsonResults.Unauthorized("Token does not belong to the current session.").AsAction();
        }

        await _users.UpdateEmailAsync(record.UserId, record.NewEmail, ct);
        return NoContent();
    }

    private static bool IsValidEmail(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 254) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(s);
            return string.Equals(addr.Address, s, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>Change the signed-in account's password. Revokes all other sessions for the user.</summary>
    [HttpPost("me/password", Name = "changeCurrentAccountPassword")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var id = CurrentId();
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Unauthorized();
        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash, user.PasswordSalt))
        {
            return JsonResults.Unauthorized("Current password did not match.").AsAction();
        }
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 12)
        {
            return JsonResults.UnprocessableEntity("Password too short", "Password must be at least 12 characters.").AsAction();
        }
        var (hash, salt) = PasswordHasher.Hash(req.NewPassword);
        await _users.UpdatePasswordAsync(id, hash, salt, ct);
        // Force fresh login on every other device.
        await _sessions.RevokeAllForUserAsync(id, ct);
        return NoContent();
    }

    public sealed record MfaSetupRequest(string Password);
    public sealed record MfaSetupResponse(string Secret, string OtpAuthUri, IReadOnlyList<string> RecoveryCodes);

    /// <summary>Begin TOTP enrollment for the signed-in account. Re-auth via password. Returns a base32 secret, otpauth URI, and one-time recovery codes (emailed to the user).</summary>
    /// <remarks>The secret is stored but not yet active; call <c>POST /api/accounts/me/mfa/verifications</c> with a code from the authenticator to activate.</remarks>
    [HttpPost("me/mfa", Name = "setupMfa")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> MfaSetup([FromBody] MfaSetupRequest req, CancellationToken ct)
    {
        var userId = CurrentId();
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        // Reauth: rotating the TOTP secret invalidates the user's authenticator
        // app and replaces all recovery codes, so require proof of the current
        // password before accepting the change. Pairs with the global CSRF
        // filter to keep this endpoint out of reach of cross-site requests.
        if (string.IsNullOrEmpty(req.Password) ||
            !PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
        {
            return JsonResults.Unauthorized("Password did not match.").AsAction();
        }

        var secret = TotpService.GenerateSecret();
        await _users.SetTotpAsync(userId, secret, enabled: false, ct);

        var codes = TotpService.GenerateRecoveryCodes();
        var hashes = codes.Select(TotpService.HashCode);
        await _recovery.ReplaceAllAsync(userId, hashes, ct);

        var issuer = _config["Bump:Security:Jwt:Issuer"] ?? "Bump";
        var uri = TotpService.OtpAuthUri(secret, user.UserEmail, issuer);
        var b32 = OtpNet.Base32Encoding.ToString(secret);

        await _mail.SendAsync(TwoFactorRecoveryCodes.Build(user.UserEmail, codes), ct);

        return Ok(new MfaSetupResponse(b32, uri, codes));
    }

    public sealed record MfaVerifyRequest(string Code);

    /// <summary>Activate TOTP by submitting a code from the authenticator app. Required after <c>POST /api/accounts/me/mfa</c>.</summary>
    [HttpPost("me/mfa/verifications", Name = "verifyMfa")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> MfaVerify([FromBody] MfaVerifyRequest req, CancellationToken ct)
    {
        var userId = CurrentId();
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TotpSecret is null)
        {
            return JsonResults.UnprocessableEntity("MFA not initialized", "Call POST /api/accounts/me/mfa first.").AsAction();
        }
        if (!TotpService.Verify(user.TotpSecret, req.Code))
        {
            return JsonResults.UnprocessableEntity("Invalid code", "TOTP code did not verify.").AsAction();
        }
        await _users.SetTotpAsync(userId, user.TotpSecret, enabled: true, ct);
        return NoContent();
    }

    public sealed record MfaDisableRequest(string Password, string Totp);

    /// <summary>Turn off TOTP for the signed-in account. Requires the current password and a valid TOTP code in the request body.</summary>
    [HttpDelete("me/mfa", Name = "disableMfa")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> MfaDisable([FromBody] MfaDisableRequest req, CancellationToken ct)
    {
        var userId = CurrentId();
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();
        if (!PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
        {
            return JsonResults.Unauthorized("Password did not match.").AsAction();
        }
        if (user.TotpSecret is null || !TotpService.Verify(user.TotpSecret, req.Totp))
        {
            return JsonResults.UnprocessableEntity("Invalid code", "TOTP code did not verify.").AsAction();
        }
        await _users.SetTotpAsync(userId, secret: null, enabled: false, ct);
        return NoContent();
    }

    private Guid CurrentId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
