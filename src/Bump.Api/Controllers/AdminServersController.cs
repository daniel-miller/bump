using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/admin/servers")]
[Tags("Servers")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class AdminServersController : ControllerBase
{
    private readonly ServerRepository _repo;

    public AdminServersController(ServerRepository repo) => _repo = repo;

    /// <summary>List the server roster for the admin UI, in roster (NATO alphabet) order.</summary>
    [HttpGet("", Name = "adminListServers")]
    [ProducesResponseType(typeof(IEnumerable<ServerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var records = await _repo.GetAllAsync();
        return JsonResults.Ok(records.Select(ServerResponse.From), ignoreNulls: true).AsAction();
    }
}
