using Microsoft.Extensions.Logging;

namespace Bump.Sdk;

public static class BumpExtensions
{
    /// <summary>
    /// Adds Bump exception reporting as an ILogger provider.
    /// Exceptions logged at Error or Critical level are automatically reported.
    /// </summary>
    public static ILoggingBuilder AddBump(
        this ILoggingBuilder builder,
        BumpOptions options,
        Func<UserContext?>? userContextFactory = null)
    {
        var reporter = new ExceptionReporter(options);
        builder.AddProvider(new BumpLoggerProvider(reporter, userContextFactory));
        return builder;
    }
}
