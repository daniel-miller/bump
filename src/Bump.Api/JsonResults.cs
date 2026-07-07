using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bump.Api;

public static class JsonResults
{
    private static readonly JsonSerializerSettings DefaultSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private static readonly JsonSerializerSettings IgnoreNullsSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public static IResult Ok(object? value, bool ignoreNulls = false) =>
        new NewtonsoftJsonResult(value, StatusCodes.Status200OK, ignoreNulls: ignoreNulls);

    public static IResult Created(string location, object? value) =>
        new NewtonsoftJsonResult(value, StatusCodes.Status201Created, location);

    public static IResult Json(object? value, int statusCode) =>
        new NewtonsoftJsonResult(value, statusCode);

    // ---- RFC 7807 problem responses ---------------------------------------

    /// <summary>
    /// Emit an RFC 7807 problem+json response.
    /// </summary>
    public static IResult Problem(
        int statusCode,
        string title,
        string? detail = null,
        string? type = null,
        string? instance = null,
        IDictionary<string, string?>? headers = null)
    {
        var body = new ProblemDetails(type, title, statusCode, detail, instance);
        return new ProblemJsonResult(body, statusCode, headers);
    }

    public static IResult BadRequest(string title, string? detail = null) =>
        Problem(StatusCodes.Status400BadRequest, title, detail);

    public static IResult Unauthorized(string detail) =>
        Problem(
            StatusCodes.Status401Unauthorized,
            title: "Authentication required",
            detail: detail,
            headers: new Dictionary<string, string?> { [HeaderNames.WWWAuthenticate] = "Bearer" });

    public static IResult NotFound(string title, string? detail = null) =>
        Problem(StatusCodes.Status404NotFound, title, detail);

    public static IResult Conflict(string title, string? detail = null) =>
        Problem(StatusCodes.Status409Conflict, title, detail);

    public static IResult UnprocessableEntity(string title, string? detail = null) =>
        Problem(StatusCodes.Status422UnprocessableEntity, title, detail);

    public static IResult TooManyRequests(string title, string? detail = null, TimeSpan? retryAfter = null)
    {
        Dictionary<string, string?>? headers = null;
        if (retryAfter is { } delay)
        {
            headers = new Dictionary<string, string?>
            {
                [HeaderNames.RetryAfter] = ((int)Math.Ceiling(delay.TotalSeconds)).ToString()
            };
        }
        return Problem(StatusCodes.Status429TooManyRequests, title, detail, headers: headers);
    }

    internal static string Serialize(object? value, bool ignoreNulls) =>
        JsonConvert.SerializeObject(value, ignoreNulls ? IgnoreNullsSettings : DefaultSettings);
}

/// <summary>
/// RFC 7807 problem-details body. Omitted fields are not written on the wire.
/// </summary>
public sealed record ProblemDetails(
    string? Type,
    string Title,
    int Status,
    string? Detail,
    string? Instance);

public sealed class NewtonsoftJsonResult(object? value, int statusCode, string? location = null, bool ignoreNulls = false) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        if (location is not null)
        {
            httpContext.Response.Headers.Location = location;
        }
        await httpContext.Response.WriteAsync(JsonResults.Serialize(value, ignoreNulls));
    }
}

public sealed class ProblemJsonResult(ProblemDetails body, int statusCode, IDictionary<string, string?>? headers) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                httpContext.Response.Headers[name] = value;
            }
        }
        // problem+json ignores nulls so omitted fields stay off the wire
        await httpContext.Response.WriteAsync(JsonResults.Serialize(body, ignoreNulls: true));
    }
}
