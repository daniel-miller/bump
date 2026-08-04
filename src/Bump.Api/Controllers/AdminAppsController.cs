using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/admin/apps")]
[Tags("Apps")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class AdminAppsController : ControllerBase
{
    private readonly AppRepository _repo;

    public AdminAppsController(AppRepository repo) => _repo = repo;

    /// <summary>List every registered app for the admin UI. Session+admin authenticated; mirrors the bearer-keyed <c>GET /api/apps</c>.</summary>
    [HttpGet("", Name = "adminListApps")]
    [ProducesResponseType(typeof(IEnumerable<AppResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var records = await _repo.GetAllAsync();
        return JsonResults.Ok(records.Select(AppResponse.From), ignoreNulls: true).AsAction();
    }

    /// <summary>Update an app's handle, name, and version. Session+admin authenticated.</summary>
    [HttpPatch("{handle}", Name = "adminUpdateApp")]
    [ProducesResponseType(typeof(AppResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(string handle, [FromBody] UpdateAppRequest request)
    {
        var validationError = request.Validate();
        if (validationError is not null) return validationError.AsAction();

        try
        {
            var record = await _repo.UpdateAsync(
                handle, request.Handle, request.Name,
                request.Major, request.Minor, request.Patch);
            return record is null
                ? JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction()
                : JsonResults.Ok(AppResponse.From(record), ignoreNulls: true).AsAction();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return JsonResults.Conflict(
                title: "Handle already in use",
                detail: $"An app with handle '{request.Handle}' already exists.").AsAction();
        }
    }
}
