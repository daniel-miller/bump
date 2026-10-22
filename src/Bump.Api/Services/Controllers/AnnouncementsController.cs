using System.Security.Claims;
using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/admin/announcements")]
[Tags("Announcements")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class AnnouncementsController : ControllerBase
{
    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal) { "info", "warning", "maintenance" };

    private readonly AnnouncementRepository _ann;
    private readonly ServerTimeZone _zone;

    public AnnouncementsController(AnnouncementRepository ann, ServerTimeZone zone)
    {
        _ann = ann;
        _zone = zone;
    }

    /// <summary>List announcements, optionally scoped to a single owner by <c>ownerId</c>.</summary>
    /// <remarks><c>publishAtLocal</c> and <c>autoHideAtLocal</c> give the same instants in the API host's time zone, for display.</remarks>
    [HttpGet(Name = "listAnnouncements")]
    public async Task<IActionResult> List([FromQuery] int? ownerId, CancellationToken ct)
    {
        var rows = await _ann.ListAsync(ownerId, ct);
        return Ok(rows.Select(View));
    }

    public sealed record CreateRequest(
        int? OwnerId, string Title, string Type, string Content,
        DateTimeOffset? PublishAt = null, DateTimeOffset? AutoHideAt = null, bool NotifySubscribers = true,
        string? PublishAtLocal = null, string? AutoHideAtLocal = null);

    /// <summary>Schedule a new announcement. <c>type</c> must be one of <c>info</c>, <c>warning</c>, or <c>maintenance</c>. If <c>ownerId</c> is null the announcement is global.</summary>
    /// <remarks>
    /// Give times as <c>publishAtLocal</c> and <c>autoHideAtLocal</c> (yyyy-MM-ddTHH:mm, no offset) and the API reads them in its own time zone, the one
    /// <c>GET /api/admin/server-time</c> names; the admin UI does this so a browser with the wrong time zone cannot shift them. <c>publishAt</c> and
    /// <c>autoHideAt</c> still take absolute instants. With neither, the announcement publishes now. Honors <c>Idempotency-Key</c>. When
    /// <c>NotifySubscribers</c> is true a notification is queued at the publish time.
    /// </remarks>
    [HttpPost(Name = "createAnnouncement")]
    [Idempotent]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        var err = Validate(req.Title, req.Type, req.Content);
        if (err is not null) return err.AsAction();

        if (!TryResolve(req.PublishAtLocal, req.PublishAt, "publish", out var publishAt, out var publishErr))
            return publishErr!.AsAction();
        if (!TryResolve(req.AutoHideAtLocal, req.AutoHideAt, "auto-hide", out var autoHideAt, out var hideErr))
            return hideErr!.AsAction();
        var publish = publishAt ?? DateTimeOffset.UtcNow;
        if (autoHideAt is { } hide && hide <= publish)
            return JsonResults.UnprocessableEntity("Invalid auto-hide time", "The auto-hide time must be after the publish time.").AsAction();

        var a = await _ann.CreateAsync(req.OwnerId, req.Title, req.Type, req.Content, publish, autoHideAt, req.NotifySubscribers, CurrentUserId(), ct);
        return Created($"/api/admin/announcements/{a.AnnouncementId}", View(a));
    }

    // A wall-clock time wins over an instant, and is read in the server's zone.
    private bool TryResolve(string? wallClock, DateTimeOffset? instant, string what, out DateTimeOffset? utc, out IResult? error)
    {
        error = null;
        utc = instant?.ToUniversalTime();
        if (string.IsNullOrWhiteSpace(wallClock)) return true;
        if (_zone.TryToUtc(wallClock, out var resolved, out var message))
        {
            utc = resolved;
            return true;
        }
        error = JsonResults.UnprocessableEntity($"Invalid {what} time", message);
        return false;
    }

    private object View(Announcement a) => new
    {
        a.AnnouncementId,
        a.OwnerId,
        a.AnnouncementTitle,
        a.AnnouncementType,
        a.AnnouncementContent,
        a.PublishAt,
        a.AutoHideAt,
        a.NotifySubscribers,
        a.DispatchedAt,
        a.CreatedBy,
        a.CreatedAt,
        a.UpdatedAt,
        PublishAtLocal = _zone.Format(a.PublishAt),
        AutoHideAtLocal = a.AutoHideAt is { } hide ? _zone.Format(hide) : null,
    };

    public sealed record UpdateRequest(string Title, string Type, string Content, DateTimeOffset PublishAt, DateTimeOffset? AutoHideAt, bool NotifySubscribers);

    /// <summary>Edit an existing announcement. All fields are overwritten.</summary>
    [HttpPatch("{id:int}", Name = "updateAnnouncement")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        var existing = await _ann.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();
        var err = Validate(req.Title, req.Type, req.Content);
        if (err is not null) return err.AsAction();
        await _ann.UpdateAsync(id, req.Title, req.Type, req.Content, req.PublishAt.ToUniversalTime(), req.AutoHideAt?.ToUniversalTime(), req.NotifySubscribers, ct);
        return NoContent();
    }

    /// <summary>Permanently delete an announcement.</summary>
    [HttpDelete("{id:int}", Name = "deleteAnnouncement")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _ann.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    private static IResult? Validate(string title, string type, string content)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return JsonResults.UnprocessableEntity("Invalid title", "Title is required, 1-200 characters.");
        if (!ValidTypes.Contains(type))
            return JsonResults.UnprocessableEntity("Invalid type", "Type must be one of: info, warning, maintenance.");
        if (string.IsNullOrWhiteSpace(content))
            return JsonResults.UnprocessableEntity("Invalid content", "Content is required.");
        return null;
    }

    private Guid? CurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(s, out var id) ? id : null;
    }
}
