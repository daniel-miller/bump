using System.Text.RegularExpressions;

namespace Bump.Api;

public sealed class App
{
    public int AppKey { get; set; }
    public string AppHandle { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public int VersionMajor { get; set; }
    public int VersionMinor { get; set; }
    public int VersionPatch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
}

public static class AppHandleRules
{
    // Lowercase letters, digits, and single hyphens; must start and end
    // with a letter or digit. Keeps URLs clean and safely routable.
    private static readonly Regex HandlePattern = new(
        @"^[a-z0-9]+(-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IResult? ValidateHandle(string? handle, string fieldName = "Handle")
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid handle",
                detail: $"{fieldName} is required.");
        }
        if (handle.Length > Limits.HandleMaxLength)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid handle",
                detail: $"{fieldName} must be {Limits.HandleMaxLength} characters or fewer.");
        }
        if (!HandlePattern.IsMatch(handle))
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid handle",
                detail: $"{fieldName} must contain only lowercase letters, digits, and single hyphens.");
        }
        return null;
    }
}

public sealed record CreateAppRequest(
    string Handle,
    string Name,
    string? Version = null
)
{
    public IResult? Validate()
    {
        var handleErr = AppHandleRules.ValidateHandle(Handle);
        if (handleErr is not null) return handleErr;

        if (string.IsNullOrWhiteSpace(Name))
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid app name",
                detail: "Name is required.");
        }
        if (Name.Length > Limits.AppNameMaxLength)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid app name",
                detail: $"Name must be {Limits.AppNameMaxLength} characters or fewer.");
        }
        if (Version is not null && !TryParseVersion(Version, out _, out _, out _))
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid version",
                detail: "Version must be in the form 'major.minor.patch' with non-negative integers.");
        }
        return null;
    }

    public (int Major, int Minor, int Patch) GetVersionOrDefault()
    {
        if (Version is not null && TryParseVersion(Version, out var major, out var minor, out var patch))
        {
            return (major, minor, patch);
        }
        return (0, 0, 1);
    }

    private static bool TryParseVersion(string s, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        return int.TryParse(parts[0], out major) && major >= 0
            && int.TryParse(parts[1], out minor) && minor >= 0
            && int.TryParse(parts[2], out patch) && patch >= 0;
    }
}

public sealed record AppResponse(
    int AppKey,
    string AppHandle,
    string AppName,
    string Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public static AppResponse From(App a) => new(
        a.AppKey,
        a.AppHandle,
        a.AppName,
        a.Version,
        a.CreatedAt,
        a.UpdatedAt
    );
}

public sealed record VersionResponse(string Version);

public sealed record SetVersionRequest(int? Major, int? Minor, int? Patch);

public sealed record UpdateAppRequest(
    string Handle,
    string Name,
    int Major,
    int Minor,
    int Patch
)
{
    public IResult? Validate()
    {
        var handleErr = AppHandleRules.ValidateHandle(Handle);
        if (handleErr is not null) return handleErr;

        if (string.IsNullOrWhiteSpace(Name))
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid app name",
                detail: "Name is required.");
        }
        if (Name.Length > Limits.AppNameMaxLength)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid app name",
                detail: $"Name must be {Limits.AppNameMaxLength} characters or fewer.");
        }
        if (Major < 0 || Minor < 0 || Patch < 0)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid version",
                detail: "Version components must be non-negative integers.");
        }
        return null;
    }
}
