using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/admin/server-time")]
[Tags("Server time")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class ServerTimeController : ControllerBase
{
    private readonly ServerTimeZone _zone;

    public ServerTimeController(ServerTimeZone zone) => _zone = zone;

    /// <summary>The API host's time zone, in which admin screens take and show wall-clock times. Session+admin authenticated.</summary>
    [HttpGet(Name = "getServerTime")]
    public IActionResult Get() => Ok(new
    {
        TimeZoneId = _zone.Zone.Id,
        _zone.DisplayName,
        Now = _zone.Format(DateTimeOffset.UtcNow),
    });
}
