using System.Security.Cryptography;
using System.Text;

namespace Bump.Api;

public static class Fingerprint
{
    public static string Compute(ProblemReportPayload payload)
    {
        var raw = $"{payload.Environment}|{payload.Application}|{payload.Type}|{payload.Title}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash)[..16];
    }
}
