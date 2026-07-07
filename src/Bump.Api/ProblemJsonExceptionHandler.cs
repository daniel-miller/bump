using Newtonsoft.Json;
using Npgsql;

namespace Bump.Api;

/// <summary>
/// Top-of-pipeline middleware that converts known exceptions thrown during
/// endpoint invocation — parameter binding, validation, or handler code —
/// into RFC 7807 <c>application/problem+json</c> responses.
///
/// This keeps input-shape errors off the framework's default HTML error page
/// so consumers always get a machine-readable body.
/// </summary>
public sealed class ProblemJsonExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemJsonExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public ProblemJsonExceptionHandler(RequestDelegate next, ILogger<ProblemJsonExceptionHandler> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (JsonException ex)
        {
            // Thrown by Newtonsoft when the body is malformed JSON or doesn't
            // match the target type. Safe to surface the message — it doesn't
            // reveal server internals, only positional parse errors.
            await WriteAsync(context, JsonResults.BadRequest(
                title: "Malformed request body",
                detail: ex.Message));
        }
        catch (BadHttpRequestException ex)
        {
            // Kestrel throws this for chunked-encoding violations, header
            // parse errors, etc. It already carries an HTTP status.
            await WriteAsync(context, JsonResults.Problem(
                ex.StatusCode,
                title: "Bad request",
                detail: ex.Message));
        }
        catch (Exception ex)
        {
            LogUnhandled(ex, context);
            if (context.Response.HasStarted)
            {
                // Too late to write a body — just let the connection close.
                throw;
            }
            // In development, surface the full exception (type, message, stack,
            // inner chain) to the client so devs can debug without tailing logs.
            // Production includes the exception type and (for Postgres) the
            // SQLSTATE plus the trace id so callers can correlate with logs
            // without leaking arbitrary internal messages.
            var detail = _env.IsDevelopment()
                ? ex.ToString()
                : BuildProdDetail(ex, context.TraceIdentifier);
            await WriteAsync(context, JsonResults.Problem(
                StatusCodes.Status500InternalServerError,
                title: "Internal server error",
                detail: detail));
        }
    }

    private static Task WriteAsync(HttpContext context, IResult result)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }
        return result.ExecuteAsync(context);
    }

    private void LogUnhandled(Exception ex, HttpContext context)
    {
        var req = context.Request;
        if (ex is PostgresException pg)
        {
            _logger.LogError(ex,
                "Unhandled exception in request pipeline. Method={Method} Path={Path} QueryString={QueryString} TraceId={TraceId} ExceptionType={ExceptionType} SqlState={SqlState} Table={Table} Column={Column} Constraint={Constraint}",
                req.Method,
                req.Path.Value,
                req.QueryString.Value,
                context.TraceIdentifier,
                ex.GetType().FullName,
                pg.SqlState,
                pg.TableName,
                pg.ColumnName,
                pg.ConstraintName);
            return;
        }
        _logger.LogError(ex,
            "Unhandled exception in request pipeline. Method={Method} Path={Path} QueryString={QueryString} TraceId={TraceId} ExceptionType={ExceptionType}",
            req.Method,
            req.Path.Value,
            req.QueryString.Value,
            context.TraceIdentifier,
            ex.GetType().FullName);
    }

    private static string BuildProdDetail(Exception ex, string traceId)
    {
        var typeName = ex.GetType().Name;
        if (ex is PostgresException pg)
        {
            var parts = new List<string> { $"{typeName} SQLSTATE {pg.SqlState}: {pg.MessageText}" };
            if (!string.IsNullOrEmpty(pg.TableName))      parts.Add($"table={pg.TableName}");
            if (!string.IsNullOrEmpty(pg.ColumnName))     parts.Add($"column={pg.ColumnName}");
            if (!string.IsNullOrEmpty(pg.ConstraintName)) parts.Add($"constraint={pg.ConstraintName}");
            if (!string.IsNullOrEmpty(pg.Hint))           parts.Add($"hint={pg.Hint}");
            parts.Add($"traceId={traceId}");
            return string.Join(" | ", parts);
        }
        var msg = ex.Message;
        return string.IsNullOrEmpty(msg)
            ? $"{typeName}. traceId={traceId}"
            : $"{typeName}: {msg}. traceId={traceId}";
    }
}
