using Microsoft.Extensions.Logging;

namespace Bump.Sdk;

public class BumpLoggerProvider : ILoggerProvider
{
    private readonly ExceptionReporter _reporter;
    private readonly Func<UserContext?>? _userContextFactory;

    public BumpLoggerProvider(ExceptionReporter reporter, Func<UserContext?>? userContextFactory = null)
    {
        _reporter = reporter;
        _userContextFactory = userContextFactory;
    }

    public ILogger CreateLogger(string categoryName) =>
        new BumpLogger(_reporter, categoryName, _userContextFactory);

    public void Dispose() { }
}

public class BumpLogger : ILogger
{
    private readonly ExceptionReporter _reporter;
    private readonly string _category;
    private readonly Func<UserContext?>? _userContextFactory;

    public BumpLogger(ExceptionReporter reporter, string category, Func<UserContext?>? userContextFactory)
    {
        _reporter = reporter;
        _category = category;
        _userContextFactory = userContextFactory;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || exception == null)
            return;

        var user = _userContextFactory?.Invoke();
        var metadata = new Dictionary<string, object>
        {
            ["category"] = _category,
            ["logLevel"] = logLevel.ToString(),
            ["message"] = formatter(state, exception)
        };

        // Fire and forget — logging must not block.
        _ = _reporter.CaptureAsync(exception, user, metadata);
    }
}
