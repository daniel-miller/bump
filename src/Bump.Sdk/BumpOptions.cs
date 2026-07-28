namespace Bump.Sdk;

public record UserContext(Guid? Id = null, string? Email = null);

public class BumpOptions
{
    /// <summary>
    /// Whether problem reports are sent. Defaults to true: reporting is the intended
    /// state, so opting out is the thing a config has to say out loud.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the endpoint. Disabling by blanking the endpoint conflates two
    /// different facts - whether to report, and where - which leaves no way to tell a consumer
    /// that was switched off on purpose from one whose deploy-time substitution failed. A consumer
    /// should treat "enabled but incompletely configured" as a startup error rather than running
    /// on silently with no reporting.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How to reach the Bump API: <c>Bump:Api:Hosting:BaseUrl</c> and
    /// <c>Bump:Api:Hosting:ClientSecret</c>.
    /// </summary>
    /// <remarks>
    /// Nested rather than flat so the shape matches every other API a service talks to -
    /// <c>&lt;system&gt;:Api:Hosting:{BaseUrl, ClientSecret}</c>. A reader who knows how one
    /// integration is configured can guess the next one.
    /// </remarks>
    public ApiSettings Api { get; set; } = new();

    public string Environment { get; set; } = "live";

    /// <summary>
    /// Slug of the bump-managed app this client corresponds to. Required so
    /// every problem report can be joined to a row in the <c>app</c> table.
    /// </summary>
    public string AppSlug { get; set; } = "";

    /// <summary>
    /// Base URL prepended to each report's <c>type</c> field, e.g.
    /// <c>https://example.com/problems/</c> turning a bare exception type name into
    /// <c>https://example.com/problems/System.NullReferenceException</c>. Leave empty to send the
    /// raw type name.
    /// </summary>
    /// <remarks>
    /// RFC 9457 §3.1.2 says <c>type</c> SHOULD be a URI, which is the point of this. It is a base
    /// URL rather than a free-text prefix - the SDK normalizes the trailing slash and joins with a
    /// path separator.
    /// <para>
    /// Per consumer, not shared. The URL space belongs to the app doing the reporting - it is
    /// where <em>that app's</em> error documentation lives - so it sits beside <see cref="AppSlug"/>
    /// as a project-local value. There is no server-side origin for it to be sourced from.
    /// </para>
    /// <para>
    /// Only worth setting if those URLs actually resolve. A prefix pointing at pages that 404
    /// satisfies the letter of the RFC while misleading anyone who clicks one, which is worse than
    /// the bare type name that promises nothing.
    /// </para>
    /// </remarks>
    public string ProblemTypeBaseUrl { get; set; } = "";

    /// <summary>
    /// HTTP status code reported with every problem unless overridden by the
    /// caller. Defaults to 500.
    /// </summary>
    public int DefaultStatus { get; set; } = 500;
}

public class ApiSettings
{
    public HostingSettings Hosting { get; set; } = new();
}

public class HostingSettings
{
    /// <summary>Base URL of the Bump API. Empty or unparseable disables reporting.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Bearer key for <c>POST /api/problems</c>.
    /// </summary>
    /// <remarks>
    /// Empty does not disable reporting - the SDK still posts, unauthenticated, and Bump answers
    /// 401. Set <see cref="BumpOptions.Enabled"/> to false to turn reporting off.
    /// </remarks>
    public string ClientSecret { get; set; } = "";
}
