using Bump.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace Bump.Api.Controllers;

[ApiController]
[ApiKeyAuthorize]
[BypassCsrf]
[EnableRateLimiting(RateLimiting.AppsPolicy)]
[Route("api/apps")]
[Tags("Apps")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class AppsController : ControllerBase
{
    private readonly AppRepository _repo;

    public AppsController(AppRepository repo)
    {
        _repo = repo;
    }

    /// <summary>Register a new app. The handle must be unique; if the optional version is omitted it defaults to 0.0.1.</summary>
    /// <remarks>Requires the Apps Bearer key. Honors <c>Idempotency-Key</c>: replays return the original 201.</remarks>
    [HttpPost("", Name = "createApp")]
    [Idempotent]
    [RequestSizeLimit(Limits.AppBodyBytes)]
    [ProducesResponseType(typeof(AppResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateAppRequest request)
    {
        var validationError = request.Validate();
        if (validationError is not null) return validationError.AsAction();

        try
        {
            var (major, minor, patch) = request.GetVersionOrDefault();
            var record = await _repo.CreateAsync(request.Handle, request.Name, major, minor, patch);
            return JsonResults.Created($"/api/apps/{record.AppHandle}", AppResponse.From(record)).AsAction();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return JsonResults.Conflict(
                title: "App already exists",
                detail: "An app with that key or handle already exists.").AsAction();
        }
    }

    /// <summary>List every registered app, ordered by handle.</summary>
    [HttpGet("", Name = "listApps")]
    [ProducesResponseType(typeof(IEnumerable<AppResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var records = await _repo.GetAllAsync();
        return JsonResults.Ok(records.Select(AppResponse.From), ignoreNulls: true).AsAction();
    }

    /// <summary>Fetch a single app by its handle.</summary>
    [HttpGet("{handle}", Name = "getApp")]
    [ProducesResponseType(typeof(AppResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string handle)
    {
        var record = await _repo.GetByHandleAsync(handle);
        return record is null
            ? JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction()
            : JsonResults.Ok(AppResponse.From(record), ignoreNulls: true).AsAction();
    }

    /// <summary>Permanently delete an app by handle.</summary>
    [HttpDelete("{handle}", Name = "deleteApp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string handle)
    {
        var deleted = await _repo.DeleteByHandleAsync(handle);
        return deleted
            ? NoContent()
            : JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction();
    }

    /// <summary>Read the current semver of an app.</summary>
    [HttpGet("{handle}/version", Name = "getAppVersion")]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(string handle)
    {
        var record = await _repo.GetByHandleAsync(handle);
        return record is null
            ? JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction()
            : JsonResults.Ok(new VersionResponse(record.Version)).AsAction();
    }

    /// <summary>Overwrite one or more semver components. Omitted components are left unchanged.</summary>
    [HttpPatch("{handle}/version", Name = "setAppVersion")]
    [RequestSizeLimit(Limits.AppBodyBytes)]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetVersion(string handle, [FromBody] SetVersionRequest request)
    {
        if (request.Major is null && request.Minor is null && request.Patch is null)
        {
            return JsonResults.UnprocessableEntity(
                title: "No version components provided",
                detail: "Provide at least one of major, minor, or patch.").AsAction();
        }

        if (request.Major is < 0 || request.Minor is < 0 || request.Patch is < 0)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid version components",
                detail: "Version components must be non-negative integers.").AsAction();
        }

        var record = await _repo.SetVersionAsync(handle, request.Major, request.Minor, request.Patch);
        return record is null
            ? JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction()
            : JsonResults.Ok(new VersionResponse(record.Version)).AsAction();
    }

    public sealed record BumpVersionRequest(string Level);

    /// <summary>Bump one component of the app's semver. Body: <c>{"level": "major"|"minor"|"patch"}</c>. Creates the app at the matching seed (1.0.0 / 0.1.0 / 0.0.1) if the handle is unknown.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost("{handle}/version/bumps", Name = "bumpAppVersion")]
    [Idempotent]
    [RequestSizeLimit(Limits.AppBodyBytes)]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BumpVersion(string handle, [FromBody] BumpVersionRequest request)
    {
        Func<string, Task<App>> op = request.Level?.ToLowerInvariant() switch
        {
            "major" => _repo.IncrementMajorAsync,
            "minor" => _repo.IncrementMinorAsync,
            "patch" => _repo.IncrementPatchAsync,
            _ => null!,
        };
        if (op is null)
        {
            return JsonResults.UnprocessableEntity(
                title: "Invalid bump level",
                detail: "Level must be one of: major, minor, patch.").AsAction();
        }

        try
        {
            var record = await op(handle);
            return JsonResults.Ok(new VersionResponse(record.Version)).AsAction();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return JsonResults.Conflict(
                title: "App already exists",
                detail: "An app with that key already exists.").AsAction();
        }
    }
}
