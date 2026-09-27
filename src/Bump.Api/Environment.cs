namespace Bump.Api;

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
