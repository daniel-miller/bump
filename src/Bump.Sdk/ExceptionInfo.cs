namespace Bump.Sdk;

/// <summary>
/// Wire-format snapshot of a thrown exception, including its chain. Sent
/// to the Bump API as the <c>Exception</c> field on a problem report.
/// </summary>
public record ExceptionInfo
{
    public string Type { get; init; } = "";
    public string Value { get; init; } = "";
    public string? StackTrace { get; init; }
    public List<ExceptionInfo>? InnerExceptions { get; init; }

    public static ExceptionInfo From(Exception ex)
    {
        var children = new List<ExceptionInfo>();

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
                children.Add(From(inner));
        }
        else if (ex.InnerException is { } inner)
        {
            children.Add(From(inner));
        }

        return new ExceptionInfo
        {
            Type = ex.GetType().FullName ?? ex.GetType().Name,
            Value = ex.Message,
            StackTrace = ex.StackTrace,
            InnerExceptions = children.Count > 0 ? children : null
        };
    }
}
