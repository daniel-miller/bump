using System.Text;
using Microsoft.Extensions.Configuration;

namespace Bump.Api.Services;

/// <summary>
/// Builds the prober User-Agent string from config. Single source of truth so
/// version/URL/contact never drift between probe headers and the /bot landing
/// page. All parts derived from Bump:Hosting:Version, Bump:Hosting:PublicBaseUrl,
/// and Bump:Services:AbuseContact.
/// </summary>
public static class BumpUserAgent
{
    public static string Build(IConfiguration config)
    {
        var version = (config["Bump:Hosting:Version"] ?? "0.0.0").Trim();
        var baseUrl = (config["Bump:Hosting:PublicBaseUrl"] ?? "").TrimEnd('/');
        var contact = config["Bump:Services:AbuseContact"];

        var sb = new StringBuilder();
        sb.Append("Bump/").Append(version);
        sb.Append(" (+").Append(baseUrl).Append("/bot");
        if (!string.IsNullOrWhiteSpace(contact))
        {
            sb.Append("; contact=").Append(contact);
        }
        sb.Append(')');
        return sb.ToString();
    }
}
