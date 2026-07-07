using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/admin/environments")]
[Tags("Environments")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class AdminEnvironmentsController : ControllerBase
{
    private readonly EnvironmentRepository _repo;

    public AdminEnvironmentsController(EnvironmentRepository repo) => _repo = repo;

    /// <summary>List every registered environment for the admin UI.</summary>
    [HttpGet("", Name = "adminListEnvironments")]
    [ProducesResponseType(typeof(IEnumerable<EnvironmentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var records = await _repo.GetAllAsync();
        return JsonResults.Ok(records.Select(EnvironmentResponse.From), ignoreNulls: true).AsAction();
    }
}
