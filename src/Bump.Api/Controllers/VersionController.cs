using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Controllers;

[ApiController]
[Route("api/version")]
[Tags("Version")]
public sealed class VersionController : ControllerBase
{
    private readonly AppRepository _apps;

    public VersionController(AppRepository apps)
    {
        _apps = apps;
    }

    /// <summary>Reports the current Bump API version as semver plus its parsed components.</summary>
    [HttpGet("", Name = "getVersion")]
    [HttpHead("")]
    [ProducesResponseType(typeof(ApiVersionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        var bump = await _apps.GetByHandleAsync("bump");
        var major = bump?.VersionMajor ?? 0;
        var minor = bump?.VersionMinor ?? 0;
        var patch = bump?.VersionPatch ?? 0;
        return JsonResults.Ok(new ApiVersionResponse(
            Version: $"{major}.{minor}.{patch}",
            Major: major,
            Minor: minor,
            Patch: patch)).AsAction();
    }
}

public sealed record ApiVersionResponse(string Version, int Major, int Minor, int Patch);
