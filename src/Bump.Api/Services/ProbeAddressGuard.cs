using System.Net;
using System.Net.Sockets;

namespace Bump.Api.Services;

/// <summary>
/// SSRF defense for service probes. Two layers:
/// <list type="number">
///   <item>
///     <see cref="TryValidateUrl"/> rejects non-http(s) schemes and
///     malformed URLs at admin-edit time and again before each probe.
///   </item>
///   <item>
///     <see cref="IsBlocked"/> is called from the worker's
///     <c>SocketsHttpHandler.ConnectCallback</c> against every address
///     resolved by DNS. A request is refused if any returned address
///     falls in a private, loopback, link-local, CGNAT, ULA, or
///     multicast range. Re-checking inside the connect callback closes
///     the DNS-rebinding window where validation passed at one point
///     in time but the host now resolves to an internal address.
///   </item>
/// </list>
/// Auto-redirect is disabled at the handler so a 30x to an internal
/// address cannot bypass the guard either; if redirect support is
/// added later, the redirected URL must be re-validated via
/// <see cref="TryValidateUrl"/>.
/// </summary>
public static class ProbeAddressGuard
{
    public static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    public static bool IsAllowedScheme(string? scheme) =>
        !string.IsNullOrEmpty(scheme) && AllowedSchemes.Contains(scheme);

    public static bool TryValidateUrl(string? url, out Uri? parsed, out string? error)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out parsed))
        {
            error = "URL must be an absolute http(s) URL.";
            parsed = null;
            return false;
        }
        if (!IsAllowedScheme(parsed.Scheme))
        {
            error = "URL scheme must be http or https.";
            return false;
        }
        error = null;
        return true;
    }

    public static bool IsBlocked(IPAddress address)
    {
        // Collapse IPv4-mapped IPv6 (::ffff:a.b.c.d) so the embedded v4
        // is matched against the v4 ranges below.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            if (b[0] == 0) return true;                                       // 0.0.0.0/8
            if (b[0] == 10) return true;                                      // 10.0.0.0/8
            if (b[0] == 127) return true;                                     // 127.0.0.0/8 (loopback)
            if (b[0] == 169 && b[1] == 254) return true;                      // 169.254.0.0/16
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;         // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;                      // 192.168.0.0/16
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;        // 100.64.0.0/10 (CGNAT)
            if (b[0] >= 224) return true;                                     // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Loopback)) return true;          // ::1
            if (address.Equals(IPAddress.IPv6Any)) return true;               // ::
            if (address.IsIPv6LinkLocal) return true;                         // fe80::/10
            if (address.IsIPv6SiteLocal) return true;                         // fec0::/10 (deprecated)
            if (address.IsIPv6Multicast) return true;                         // ff00::/8

            // Unique Local Address fc00::/7 — first byte 0xFC or 0xFD.
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            return false;
        }

        // Unknown family — refuse rather than guess.
        return true;
    }
}
