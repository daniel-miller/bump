namespace Bump.Api;

/// <summary>
/// Wire-format snapshot of a thrown exception, including its chain.
/// AggregateException's InnerExceptions and a plain InnerException are both
/// stored as children in <see cref="InnerExceptions"/>.
/// </summary>
public record ExceptionInfo
{
    public string Type { get; init; } = "";
    public string Value { get; init; } = "";
    public string? StackTrace { get; init; }
    public List<ExceptionInfo>? InnerExceptions { get; init; }
}
