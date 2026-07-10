namespace Bump.Api;

/// <summary>
/// Upper bounds on untrusted input. Keeping these in one place makes it
/// easier to audit what we accept — and to keep the API, the database, and
/// the SDK agreeing on the same caps.
///
/// The body-byte limits are enforced via [RequestSizeLimit] on each action.
/// The field-character limits are enforced per-DTO in <c>Validate()</c>.
/// Column widths in <c>db/migrations/*.sql</c> are sized at least as wide as these caps.
/// </summary>
public static class Limits
{
    // ---- Request body sizes -----------------------------------------------

    /// <summary>Max bytes for an app create / set-version body.</summary>
    public const int AppBodyBytes = 4 * 1024;

    /// <summary>
    /// Max bytes for a problem report body. Stack traces + extensions can be
    /// chunky, but well-formed reports fit comfortably under this.
    /// </summary>
    public const int ProblemBodyBytes = 64 * 1024;

    // ---- Field character lengths ------------------------------------------

    public const int SlugMaxLength    = 50;
    public const int AppNameMaxLength = 200;

    public const int EnvironmentMaxLength = 100;

    public const int ProblemTypeMaxLength     = 500;
    public const int ProblemTitleMaxLength    = 500;
    public const int ProblemInstanceMaxLength = 2048;
    public const int ProblemDetailMaxLength   = 10_000;

    /// <summary>Max serialized size of the Exception graph.</summary>
    public const int ExceptionJsonMaxLength   = 64_000;

    public const int UserEmailMaxLength = 254;

    /// <summary>Max serialized size of the Extensions bag.</summary>
    public const int ExtensionsJsonMaxLength = 16_000;
}
