using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/health")]
[Tags("Health")]
public sealed class HealthController : ControllerBase
{
    private readonly NpgsqlDataSource _db;

    public HealthController(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Liveness probe. Returns <c>healthy</c> if the database is reachable, <c>unhealthy</c> with 503 otherwise. Supports HEAD for lightweight probes.</summary>
    [HttpGet("", Name = "getHealth")]
    [HttpHead("")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get()
    {
        try
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            return JsonResults.Ok(new HealthResponse("healthy")).AsAction();
        }
        catch
        {
            return JsonResults.Json(new HealthResponse("unhealthy"), 503).AsAction();
        }
    }
}
