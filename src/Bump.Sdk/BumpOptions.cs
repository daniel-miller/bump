namespace Bump.Sdk;

public record UserContext(Guid? Id = null, string? Email = null);

public class BumpOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Environment { get; set; } = "live";

    /// <summary>
    /// Slug of the bump-managed app this client corresponds to. Required so
    /// every problem report can be joined to a row in the <c>app</c> table.
    /// </summary>
    public string AppSlug { get; set; } = "";

    /// <summary>
    /// Base URI prepended to each report's <c>type</c> field. RFC 9457 §3.1.2
    /// says <c>type</c> SHOULD be a URI; setting this to e.g.
    /// "https://example.com/problems/" turns a bare exception type name into a
    /// dereferenceable URI (e.g. "https://example.com/problems/System.NullReferenceException").
    /// Leave empty to send the raw type name.
    /// </summary>
    public string ProblemTypePrefix { get; set; } = "";

    /// <summary>
    /// HTTP status code reported with every problem unless overridden by the
    /// caller. Defaults to 500.
    /// </summary>
    public int DefaultStatus { get; set; } = 500;
}
