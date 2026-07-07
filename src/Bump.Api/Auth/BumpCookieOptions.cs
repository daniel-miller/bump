namespace Bump.Api.Auth;

/// <summary>
/// Central cookie config. Defaults match the production deployment (cross-
/// origin React on Cloudflare Pages → Railway API), but Development can
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
        var section = config.GetSection("Bump:Security:Cookie");
        return new BumpCookieOptions
        {
            Domain = section["Domain"],
            SameSite = section["SameSite"] ?? "None",
            Secure = !bool.TryParse(section["Secure"], out var s) || s
        };
    }
}
