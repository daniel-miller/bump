namespace Bump.Api;

public sealed class EnvironmentRecord
{
    public int EnvironmentKey { get; set; }
    public string EnvironmentSlug { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string? EnvironmentDescription { get; set; }
    public string[] EnvironmentAliases { get; set; } = Array.Empty<string>();
    public bool IsSpecialPurpose { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed record EnvironmentResponse(
    int EnvironmentKey,
    string EnvironmentSlug,
    string EnvironmentName,
    string? EnvironmentDescription,
    string[] EnvironmentAliases,
    bool IsSpecialPurpose,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public static EnvironmentResponse From(EnvironmentRecord e) => new(
        e.EnvironmentKey,
        e.EnvironmentSlug,
        e.EnvironmentName,
        e.EnvironmentDescription,
        e.EnvironmentAliases,
        e.IsSpecialPurpose,
        e.CreatedAt,
        e.UpdatedAt
    );
}
