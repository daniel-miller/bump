using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Bump.Api.Auth;

/// <summary>
/// HS256 JWT issuer/validator. Tokens carry sub=account_id, sid=session_id,
/// iat, exp. The sid is the link to <c>account_session</c>, which is the
/// authoritative revocation store.
/// </summary>
public sealed class JwtIssuer
{
    static JwtIssuer()
    {
        // Stop the handler renaming "sub" -> ClaimTypes.NameIdentifier on read.
        // We want raw JWT claim names so issuance and validation see the same keys.
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
    }

    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _lifetime;

    public JwtIssuer(IConfiguration config)
    {
        var signing = config["Bump:Security:Jwt:Signing"];
        if (string.IsNullOrWhiteSpace(signing))
        {
            throw new InvalidOperationException(
                "Bump:Security:Jwt:Signing is empty. Provide a base64-encoded key of at least 32 bytes via config/appsettings.work.json or the Bump__Security__Jwt__Signing environment variable.");
        }
        var keyBytes = Convert.FromBase64String(signing);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("Bump:Security:Jwt:Signing must decode to at least 32 bytes.");
        }
        _key = new SymmetricSecurityKey(keyBytes);
        _issuer = config["Bump:Security:Jwt:Issuer"] ?? "bump-api";
        _audience = config["Bump:Security:Jwt:Audience"] ?? "bump-web";
        _lifetime = TimeSpan.FromDays(14);
    }

    public TimeSpan Lifetime => _lifetime;

    public (string Token, Guid SessionId, DateTimeOffset ExpiresAt) Issue(Guid userId)
    {
        var sessionId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.Add(_lifetime);
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("sid", sessionId.ToString())
            },
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return (jwt, sessionId, expires);
    }

    public bool TryValidate(string jwt, out Guid userId, out Guid sessionId)
    {
        userId = Guid.Empty;
        sessionId = Guid.Empty;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(jwt, new TokenValidationParameters
            {
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = _key,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var sid = principal.FindFirstValue("sid");
            return Guid.TryParse(sub, out userId) && Guid.TryParse(sid, out sessionId);
        }
        catch
        {
            return false;
        }
    }

    public static byte[] HashSessionId(Guid sessionId) =>
        SHA256.HashData(sessionId.ToByteArray());
}
