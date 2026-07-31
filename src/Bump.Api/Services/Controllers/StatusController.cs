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
    private readonly BoardRepository _boards;
    private readonly ITimezoneResolver _tz;

    public StatusController(StatusComposer composer, AnnouncementRepository ann, BoardRepository boards, ITimezoneResolver tz)
    {
        _composer = composer;
        _ann = ann;
        _boards = boards;
        _tz = tz;
    }

    /// <summary>Aggregate public status payload covering every tenant.</summary>
    /// <remarks>Anonymous endpoint. Set <c>excludePaused=true</c> to omit paused services from the response.</remarks>
    [HttpGet(Name = "getGlobalStatus")]
    public async Task<IActionResult> Get([FromQuery] bool excludePaused = false, CancellationToken ct = default)
    {
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var payload = await _composer.ComposeAsync(null, excludePaused, tz, ct);
        return Ok(payload);
    }

    /// <summary>Public status payload scoped to one tenant.</summary>
    /// <remarks>Anonymous endpoint. Set <c>excludePaused=true</c> to omit paused services from the response.</remarks>
    [HttpGet("tenants/{slug}", Name = "getTenantStatus")]
    public async Task<IActionResult> GetForTenant(string slug, [FromQuery] bool excludePaused = false, CancellationToken ct = default)
    {
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var payload = await _composer.ComposeAsync(slug, excludePaused, tz, ct);
        return Ok(payload);
    }

    /// <summary>Resolve the request's Host header to a tenant with a matching custom hostname.</summary>
    /// <remarks>Anonymous endpoint. Returns 404 when the host is not assigned to any tenant,
    /// which tells the SPA to fall back to its default root behavior.</remarks>
    [HttpGet("site", Name = "getSiteTenant")]
    public async Task<IActionResult> Site(CancellationToken ct)
    {
        var host = Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return NotFound();
        var b = await _boards.GetByHostAsync(host, ct);
        if (b is null) return NotFound();
        return Ok(new
        {
            boardSlug = b.BoardSlug,
            boardName = b.BoardName,
            theme = b.BoardTheme is null ? null : (object)JToken.Parse(b.BoardTheme),
        });
    }

    /// <summary>Currently-visible global announcements (not scoped to any tenant).</summary>
    [HttpGet("global/announcements", Name = "getGlobalAnnouncements")]
    public async Task<IActionResult> Announcements(CancellationToken ct)
    {
        var rows = await _ann.ListVisibleAsync(null, DateTimeOffset.UtcNow, ct);
        return Ok(rows.Select(a => new { a.AnnouncementId, a.AnnouncementTitle, a.AnnouncementType, a.AnnouncementContent, a.PublishAt, a.AutoHideAt }));
    }

    /// <summary>Currently-visible announcements for one tenant.</summary>
    [HttpGet("tenants/{slug}/announcements", Name = "getTenantAnnouncements")]
    public async Task<IActionResult> AnnouncementsForTenant(string slug, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var rows = await _ann.ListVisibleAsync(b.BoardId, DateTimeOffset.UtcNow, ct);
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
