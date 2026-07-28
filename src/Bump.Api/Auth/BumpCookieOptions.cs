namespace Bump.Api.Auth;

/// <summary>
/// Central cookie config. Defaults match the production deployment (cross-
/// origin React on Cloudflare Pages against a Railway API), but Development can
/// override Domain/SameSite/Secure to make cookies work over plain HTTP at
/// localhost.
/// </summary>
public sealed class BumpCookieOptions
{
    public string SessionCookieName { get; init; } = "bump_session";
    public string CsrfCookieName { get; init; } = "bump_csrf";
    public string? Domain { get; init; }
    public string SameSite { get; init; } = "None";
    public bool Secure { get; init; } = true;

    public SameSiteMode SameSiteMode => SameSite.Equals("Lax", StringComparison.OrdinalIgnoreCase)
        ? SameSiteMode.Lax
        : SameSite.Equals("Strict", StringComparison.OrdinalIgnoreCase)
            ? SameSiteMode.Strict
            : SameSiteMode.None;

    public static BumpCookieOptions FromConfig(IConfiguration config)
    {
        var section = config.GetSection("Bump:Api:Security:Cookie");
        var domain = section["Domain"];
        return new BumpCookieOptions
        {
            // Normalized to null so "unset" has one representation. The config file
            // carries "" (JSON null reads as a deliberate sentinel when it only means
            // unset), but CookieOptions.Domain = "" emits a bare `domain=` attribute
            // that browsers reject - which would silently stop logout from clearing
            // the session cookie, since the delete path assigns Domain unguarded.
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            SameSite = section["SameSite"] ?? "None",
            Secure = !bool.TryParse(section["Secure"], out var s) || s
        };
    }
}
