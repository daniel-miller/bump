namespace Bump.Api;

/// <summary>
/// Tokens whose meaning moved to a different environment. Consulted for lookup
/// only and never stored or displayed, so this does not make "demo" an alias
/// of Stage. "demo" named the pre-production gate until 2026-07-31 and now
/// names the demonstration environment; deployed apps still report the old
/// value, so a bare "demo" resolves to Stage until their config is migrated.
/// Delete this once no deployment reports the old value.
/// </summary>
public static class EnvironmentTokens
{
    private static readonly Dictionary<string, string> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demo"] = "stage",
    };

    public static string Resolve(string token) =>
        Legacy.TryGetValue(token, out var canonical) ? canonical : token;
}

public sealed class EnvironmentRecord
{
    public int EnvironmentKey { get; set; }
    public short? EnvironmentNumber { get; set; }
    public string EnvironmentHandle { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string? EnvironmentDescription { get; set; }
    public string[] EnvironmentAliases { get; set; } = Array.Empty<string>();
    public bool IsSpecialPurpose { get; set; }
    public bool IsDerivedFromLive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed record EnvironmentResponse(
    int EnvironmentKey,
    short? EnvironmentNumber,
    string EnvironmentHandle,
    string EnvironmentName,
    string? EnvironmentDescription,
    string[] EnvironmentAliases,
    bool IsSpecialPurpose,
    bool IsDerivedFromLive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public static EnvironmentResponse From(EnvironmentRecord e) => new(
        e.EnvironmentKey,
        e.EnvironmentNumber,
        e.EnvironmentHandle,
        e.EnvironmentName,
        e.EnvironmentDescription,
        e.EnvironmentAliases,
        e.IsSpecialPurpose,
        e.IsDerivedFromLive,
        e.CreatedAt,
        e.UpdatedAt
    );
}
