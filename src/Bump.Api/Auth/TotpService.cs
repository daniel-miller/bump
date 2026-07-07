using System.Security.Cryptography;
using System.Web;
using OtpNet;

namespace Bump.Api.Auth;

/// <summary>
/// TOTP issuance and verification (RFC 6238, 30s step, 6 digits, SHA-1).
/// Secret bytes stored raw in <c>account.totp_secret</c>; encoded as
/// base32 in the otpauth URI shown to authenticator apps.
/// </summary>
public static class TotpService
{
    public static byte[] GenerateSecret() => KeyGeneration.GenerateRandomKey(20);

    public static string OtpAuthUri(byte[] secret, string accountEmail, string issuer)
    {
        var b32 = Base32Encoding.ToString(secret);
        var label = HttpUtility.UrlEncode($"{issuer}:{accountEmail}");
        var iss = HttpUtility.UrlEncode(issuer);
        return $"otpauth://totp/{label}?secret={b32}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }

    public static bool Verify(byte[] secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var totp = new Totp(secret);
        return totp.VerifyTotp(code.Trim(), out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }

    public static IReadOnlyList<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(8);
            // Format: XXXX-XXXX-XXXX-XXXX (16 hex chars chunked).
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            codes.Add($"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}");
        }
        return codes;
    }

    public static byte[] HashCode(string code) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code.Trim().ToLowerInvariant()));
}
