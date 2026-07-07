using Newtonsoft.Json;

namespace Bump.Api;

public record ProblemReportPayload
{
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public Dictionary<string, object>? Extensions { get; init; }

    public string Environment { get; init; } = "";
    public ExceptionInfo? Exception { get; init; }

    public string Application { get; init; } = "";

    public Guid? UserId { get; init; }
    public string? UserEmail { get; init; }

    /// <summary>
    /// Shape-level validation. Returns a problem+json error IResult if a
    /// required field is missing or a string field exceeds its cap;
    /// otherwise returns null.
    /// </summary>
    public IResult? Validate()
    {
        if (string.IsNullOrWhiteSpace(Type))
            return Err("Invalid problem report", "Type is required.");
        if (Type.Length > Limits.ProblemTypeMaxLength)
            return TooLong("Type", Limits.ProblemTypeMaxLength);

        if (string.IsNullOrWhiteSpace(Title))
            return Err("Invalid problem report", "Title is required.");
        if (Title.Length > Limits.ProblemTitleMaxLength)
            return TooLong("Title", Limits.ProblemTitleMaxLength);

        if (Detail is { Length: > Limits.ProblemDetailMaxLength })
            return TooLong("Detail", Limits.ProblemDetailMaxLength);

        if (Instance is { Length: > Limits.ProblemInstanceMaxLength })
            return TooLong("Instance", Limits.ProblemInstanceMaxLength);

        if (string.IsNullOrWhiteSpace(Environment))
            return Err("Invalid problem report", "Environment is required.");
        if (Environment.Length > Limits.EnvironmentMaxLength)
            return TooLong("Environment", Limits.EnvironmentMaxLength);

        var slugErr = AppSlugRules.ValidateSlug(Application, "Application");
        if (slugErr is not null) return slugErr;

        if (Exception is not null)
        {
            var serialized = JsonConvert.SerializeObject(Exception);
            if (serialized.Length > Limits.ExceptionJsonMaxLength)
                return TooLong("Exception", Limits.ExceptionJsonMaxLength);
        }

        if (UserEmail is { Length: > Limits.UserEmailMaxLength })
            return TooLong("UserEmail", Limits.UserEmailMaxLength);

        if (Extensions is not null)
        {
            // Rough size check so a caller can't push a 5 MB extensions bag
            // through a 64 KB body cap by submitting tightly-packed JSON.
            var serialized = JsonConvert.SerializeObject(Extensions);
            if (serialized.Length > Limits.ExtensionsJsonMaxLength)
                return TooLong("Extensions", Limits.ExtensionsJsonMaxLength);
        }

        return null;
    }

    private static IResult Err(string title, string detail) =>
        JsonResults.UnprocessableEntity(title, detail);

    private static IResult TooLong(string field, int max) =>
        JsonResults.UnprocessableEntity(
            title: "Field too long",
            detail: $"{field} must be {max} characters or fewer.");
}

public record ProblemReportRecord
{
    public long ProblemKey { get; init; }
    public string Fingerprint { get; init; } = "";
    public DateTime ReportedAt { get; init; }
    public DateTime? DispatchedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }

    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public string? Extensions { get; init; }

    public string AppSlug { get; init; } = "";
    public string AppName { get; init; } = "";
    public string AppVersion { get; init; } = "";

    public string Environment { get; init; } = "";
    public string EnvironmentName { get; init; } = "";
    public string? EnvironmentDescription { get; init; }

    public ExceptionInfo? Exception { get; init; }

    public Guid? UserId { get; init; }
    public string? UserEmail { get; init; }
}

public class ProblemReportFilter
{
    public string? Environment { get; set; }
    public string? AppSlug { get; set; }
    public string? Fingerprint { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; } = 0;
    public bool IncludeResolved { get; set; } = false;
}
