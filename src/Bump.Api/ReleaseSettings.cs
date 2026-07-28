namespace Bump.Api;

/// <summary>
/// Facts fixed at deploy time by the release pipeline, bound from the root <c>Release</c>
/// section. System-wide rather than service-owned: Bump.Api and Bump.Worker read the same
/// two values from the same path.
/// </summary>
/// <remarks>
/// Membership test for this section: the deploy pipeline knows it, and it does not vary per
/// request. Anything else belongs to the service that owns it.
/// </remarks>
public sealed class ReleaseSettings
{
    /// <summary>
    /// Reporting label for the deployment - <c>work</c>, <c>demo</c>, <c>test</c>, <c>live</c>.
    /// </summary>
    /// <remarks>
    /// Not <c>ASPNETCORE_ENVIRONMENT</c>, which is a behaviour switch: under Development the
    /// SPA fallback, Swagger exposure, and cookie policy all change. This one only labels
    /// output - the About page and, once Bump reports to itself, the environment stamped on
    /// every problem report.
    /// </remarks>
    public string Environment { get; set; } = "";

    /// <summary>
    /// The deployed semver, pushed to the <c>bump</c> app row at startup and surfaced in the
    /// probe user agent and on the About page.
    /// </summary>
    /// <remarks>
    /// Deliberately not the assembly version. This is deployment identity - which release was
    /// installed - and the About page shows both so a mismatch is visible.
    /// </remarks>
    public string Version { get; set; } = "";
}
