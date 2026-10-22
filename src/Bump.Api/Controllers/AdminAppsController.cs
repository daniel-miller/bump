using Bump.Api.Auth;
using Bump.Api.Services;
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
    private readonly OwnerRepository _owners;

    public AdminAppsController(AppRepository repo, OwnerRepository owners)
    {
        _repo = repo;
        _owners = owners;
    }

    /// <summary>List every registered app for the admin UI. Session+admin authenticated; mirrors the bearer-keyed <c>GET /api/apps</c>.</summary>
    [HttpGet("", Name = "adminListApps")]
    [ProducesResponseType(typeof(IEnumerable<AppResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var records = await _repo.GetAllAsync();
        return JsonResults.Ok(records.Select(AppResponse.From), ignoreNulls: true).AsAction();
    }

    /// <summary>Fetch a single app by its handle for the admin UI. Session+admin authenticated; mirrors the bearer-keyed <c>GET /api/apps/{handle}</c>.</summary>
    [HttpGet("{handle}", Name = "adminGetApp")]
    [ProducesResponseType(typeof(AppResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string handle)
    {
        var record = await _repo.GetByHandleAsync(handle);
        return record is null
            ? JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction()
            : JsonResults.Ok(AppResponse.From(record), ignoreNulls: true).AsAction();
    }

    /// <summary>Update an app's handle, name, version, owner, and aliases. Session+admin authenticated.</summary>
    /// <remarks>Omit <c>owner</c> or <c>aliases</c> to leave them unchanged. An alias is a former handle still accepted from reporters, and may not be any other app's handle or alias.</remarks>
    [HttpPatch("{handle}", Name = "adminUpdateApp")]
    [ProducesResponseType(typeof(AppResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(string handle, [FromBody] UpdateAppRequest request)
    {
        var validationError = request.Validate();
        if (validationError is not null) return validationError.AsAction();

        var current = await _repo.GetByHandleAsync(handle);
        if (current is null)
            return JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction();

        if (request.Owner is not null && await _owners.GetByHandleAsync(request.Owner) is null)
        {
            return JsonResults.UnprocessableEntity(
                title: "Unknown owner",
                detail: $"No owner has the handle '{request.Owner}'.").AsAction();
        }

        var tokens = new[] { request.Handle }.Concat(request.Aliases ?? []);
        var taken = await _repo.FindTakenAsync(tokens, exceptKey: current.AppKey);
        if (taken is not null)
        {
            return JsonResults.Conflict(
                title: "Handle already in use",
                detail: $"Another app already answers to '{taken}', as its handle or an alias.").AsAction();
        }

        try
        {
            var record = await _repo.UpdateAsync(
                handle, request.Handle, request.Name,
                request.Major, request.Minor, request.Patch,
                request.Owner, request.Aliases);
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

    /// <summary>
    /// Permanently delete an app. Problem reports filed against it cascade away with it.
    /// Refused with 409 while any service still belongs to the app. Session+admin
    /// authenticated; mirrors the bearer-keyed <c>DELETE /api/apps/{handle}</c>.
    /// </summary>
    [HttpDelete("{handle}", Name = "adminDeleteApp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string handle)
    {
        try
        {
            var deleted = await _repo.DeleteByHandleAsync(handle);
            return deleted
                ? NoContent()
                : JsonResults.NotFound("App not found", $"No app with handle '{handle}'.").AsAction();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.RestrictViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return AppHasServices(handle).AsAction();
        }
    }

    internal static IResult AppHasServices(string handle) => JsonResults.Conflict(
        title: "App has services",
        detail: $"Services still belong to '{handle}'. Move them to another app before deleting it.");
}
