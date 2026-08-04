using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/status")]
[Tags("Status")]
[AllowAnonymousFlag]
public sealed class StatusController : ControllerBase
{
    private readonly StatusComposer _composer;
    private readonly AnnouncementRepository _ann;
    private readonly OwnerRepository _owners;
    private readonly ITimezoneResolver _tz;

    public StatusController(StatusComposer composer, AnnouncementRepository ann, OwnerRepository owners, ITimezoneResolver tz)
    {
        _composer = composer;
        _ann = ann;
        _owners = owners;
        _tz = tz;
    }

    /// <summary>Aggregate public status payload covering every owner.</summary>
    /// <remarks>Anonymous endpoint. Set <c>excludePaused=true</c> to omit paused services from the response.</remarks>
    [HttpGet(Name = "getGlobalStatus")]
    public async Task<IActionResult> Get([FromQuery] bool excludePaused = false, CancellationToken ct = default)
    {
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var payload = await _composer.ComposeAsync(null, excludePaused, tz, ct);
        return Ok(payload);
    }

    /// <summary>Public status payload scoped to one owner.</summary>
    /// <remarks>Anonymous endpoint. Set <c>excludePaused=true</c> to omit paused services from the response.</remarks>
    [HttpGet("owners/{handle}", Name = "getOwnerStatus")]
    public async Task<IActionResult> GetForOwner(string handle, [FromQuery] bool excludePaused = false, CancellationToken ct = default)
    {
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var payload = await _composer.ComposeAsync(handle, excludePaused, tz, ct);
        return Ok(payload);
    }

    /// <summary>Resolve the request's Host header to an owner with a matching custom hostname.</summary>
    /// <remarks>Anonymous endpoint. Returns 404 when the host is not assigned to any owner,
    /// which tells the SPA to fall back to its default root behavior.</remarks>
    [HttpGet("site", Name = "getSiteOwner")]
    public async Task<IActionResult> Site(CancellationToken ct)
    {
        var host = Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return NotFound();
        var o = await _owners.GetByHostAsync(host, ct);
        if (o is null) return NotFound();
        return Ok(new
        {
            ownerHandle = o.OwnerHandle,
            ownerName = o.OwnerName,
            theme = o.OwnerTheme is null ? null : (object)JToken.Parse(o.OwnerTheme),
        });
    }

    /// <summary>Currently-visible global announcements (not scoped to any owner).</summary>
    [HttpGet("global/announcements", Name = "getGlobalAnnouncements")]
    public async Task<IActionResult> Announcements(CancellationToken ct)
    {
        var rows = await _ann.ListVisibleAsync(null, DateTimeOffset.UtcNow, ct);
        return Ok(rows.Select(a => new { a.AnnouncementId, a.AnnouncementTitle, a.AnnouncementType, a.AnnouncementContent, a.PublishAt, a.AutoHideAt }));
    }

    /// <summary>Currently-visible announcements for one owner.</summary>
    [HttpGet("owners/{handle}/announcements", Name = "getOwnerAnnouncements")]
    public async Task<IActionResult> AnnouncementsForOwner(string handle, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var rows = await _ann.ListVisibleAsync(o.OwnerId, DateTimeOffset.UtcNow, ct);
        return Ok(rows.Select(a => new { a.AnnouncementId, a.AnnouncementTitle, a.AnnouncementType, a.AnnouncementContent, a.PublishAt, a.AutoHideAt }));
    }
}

/// <summary>
/// Marker so [Authorize] is not required on the public status endpoints.
/// Pipeline default authorization is per-controller; absent of [Authorize]
/// the endpoint is reachable anonymously.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class AllowAnonymousFlagAttribute : Attribute { }
