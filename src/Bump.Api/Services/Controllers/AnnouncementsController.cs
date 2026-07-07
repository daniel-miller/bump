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

    public AnnouncementsController(AnnouncementRepository ann) => _ann = ann;

    /// <summary>List announcements, optionally scoped to a single tenant by <c>boardId</c>.</summary>
    [HttpGet(Name = "listAnnouncements")]
    public async Task<IActionResult> List([FromQuery] int? boardId, CancellationToken ct)
    {
        var rows = await _ann.ListAsync(boardId, ct);
        return Ok(rows);
    }

    public sealed record CreateRequest(int? BoardId, string Title, string Type, string Content, DateTimeOffset PublishAt, DateTimeOffset? AutoHideAt, bool NotifySubscribers = true);

    /// <summary>Schedule a new announcement. <c>type</c> must be one of <c>info</c>, <c>warning</c>, or <c>maintenance</c>. If <c>boardId</c> is null the announcement is global.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>. When <c>NotifySubscribers</c> is true a notification is queued at <c>PublishAt</c>.</remarks>
    [HttpPost(Name = "createAnnouncement")]
    [Idempotent]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        var err = Validate(req.Title, req.Type, req.Content);
        if (err is not null) return err.AsAction();
        var a = await _ann.CreateAsync(req.BoardId, req.Title, req.Type, req.Content, req.PublishAt.ToUniversalTime(), req.AutoHideAt?.ToUniversalTime(), req.NotifySubscribers, CurrentUserId(), ct);
        return Created($"/api/admin/announcements/{a.AnnouncementId}", a);
    }

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
