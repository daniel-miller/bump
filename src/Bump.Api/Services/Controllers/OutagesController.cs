using System.Security.Claims;
using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/admin/outages")]
[Tags("Outages")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class OutagesController : ControllerBase
{
    private readonly OutageRepository _outages;
    private readonly ServiceRepository _services;

    public OutagesController(OutageRepository outages, ServiceRepository services)
    {
        _outages = outages;
        _services = services;
    }

    /// <summary>List outages. <c>status</c> may be <c>all</c> (default), <c>open</c>, or any outage status. Capped to 500 per page.</summary>
    [HttpGet(Name = "listOutages")]
    public async Task<IActionResult> List([FromQuery] string status = "all", [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        var rows = await _outages.ListAsync(status, limit, offset, ct);
        return Ok(rows);
    }

    public sealed record CreateOutageRequest(int? ServiceId, string Title, string? Region, string? RootCause);

    /// <summary>Open a new outage. Pass <c>serviceId</c> to attach it to a specific service; omit for a manual/non-service outage.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost(Name = "createOutage")]
    [Idempotent]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateOutageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
        {
            return JsonResults.UnprocessableEntity("Invalid title", "Title is required, 1-200 characters.").AsAction();
        }
        var outage = await _outages.CreateAsync(req.ServiceId, req.Title, req.Region, req.RootCause, autoCreated: false, createdBy: CurrentUserId(), ct);
        return Created($"/api/admin/outages/{outage.OutageId}", outage);
    }

    /// <summary>Fetch an outage along with every status update appended to it.</summary>
    [HttpGet("{id:int}", Name = "getOutage")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var outage = await _outages.GetByIdAsync(id, ct);
        if (outage is null) return NotFound();
        var updates = await _outages.GetUpdatesAsync(id, ct);
        object? service = null;
        if (outage.ServiceId is int sid)
        {
            var svc = await _services.GetByIdAsync(sid, ct);
            if (svc is not null)
            {
                service = new { id = svc.ServiceId, handle = svc.ServiceHandle, name = svc.ServiceName, url = svc.ServiceUrl };
            }
        }
        return Ok(new { outage, updates, service });
    }

    public sealed record UpdateOutageRequest(string Title, string? Region, string? RootCause);

    /// <summary>Edit outage metadata (title, region, root cause). Use the <c>/updates</c> endpoint to append status updates instead.</summary>
    [HttpPatch("{id:int}", Name = "updateOutage")]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateOutageRequest req, CancellationToken ct)
    {
        var outage = await _outages.GetByIdAsync(id, ct);
        if (outage is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
        {
            return JsonResults.UnprocessableEntity("Invalid title", "Title is required, 1-200 characters.").AsAction();
        }
        await _outages.UpdateAsync(id, req.Title, req.Region, req.RootCause, ct);
        return NoContent();
    }

    public sealed record AppendUpdateRequest(string Status, string Message, bool Published = true);

    /// <summary>Append a status update (investigating, identified, monitoring, resolved, ...) to an outage.</summary>
    /// <remarks>Set <c>published</c> to false for internal-only notes that subscribers will not be notified about. Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost("{id:int}/updates", Name = "addOutageUpdate")]
    [Idempotent]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> AddUpdate(int id, [FromBody] AppendUpdateRequest req, CancellationToken ct)
    {
        if (!OutageStatuses.All.Contains(req.Status))
        {
            return JsonResults.UnprocessableEntity("Invalid status", $"Status must be one of: {string.Join(", ", OutageStatuses.All)}.").AsAction();
        }
        if (string.IsNullOrWhiteSpace(req.Message) || req.Message.Length > 2000)
        {
            return JsonResults.UnprocessableEntity("Invalid message", "Message is required, 1-2000 characters.").AsAction();
        }
        var outage = await _outages.GetByIdAsync(id, ct);
        if (outage is null) return NotFound();
        var update = await _outages.AppendUpdateAsync(id, req.Status, req.Message, req.Published, CurrentUserId(), ct);
        return Created($"/api/admin/outages/{id}/updates/{update.UpdateId}", update);
    }

    public sealed record ResolveRequest(string Message);

    /// <summary>Close an outage by appending a final <c>resolved</c> status update with the given message.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost("{id:int}/resolve", Name = "resolveOutage")]
    [Idempotent]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Resolve(int id, [FromBody] ResolveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message) || req.Message.Length > 2000)
        {
            return JsonResults.UnprocessableEntity("Invalid message", "Message is required.").AsAction();
        }
        var outage = await _outages.GetByIdAsync(id, ct);
        if (outage is null) return NotFound();
        await _outages.AppendUpdateAsync(id, OutageStatuses.Resolved, req.Message, published: true, createdBy: CurrentUserId(), ct);
        return NoContent();
    }

    /// <summary>Delete an outage and all its updates. This cannot be undone.</summary>
    [HttpDelete("{id:int}", Name = "deleteOutage")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _outages.DeleteAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private Guid? CurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(s, out var id) ? id : null;
    }
}
