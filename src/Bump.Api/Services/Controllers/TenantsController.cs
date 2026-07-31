using Bump.Api.Auth;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/admin/tenants")]
[Tags("Tenants")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class TenantsController : ControllerBase
{
    private readonly BoardRepository _boards;
    private readonly SubscriberRepository _subscribers;
    private readonly IMailgunClient _mail;
    private readonly IConfiguration _config;
    private readonly SubscribersSettings _settings;

    public TenantsController(
        BoardRepository boards,
        SubscriberRepository subscribers,
        IMailgunClient mail,
        IConfiguration config,
        SubscribersSettings settings)
    {
        _boards = boards;
        _subscribers = subscribers;
        _mail = mail;
        _config = config;
        _settings = settings;
    }

    /// <summary>List every tenant (status page grouping).</summary>
    [HttpGet(Name = "listTenants")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _boards.ListAsync(ct);
        return Ok(rows);
    }

    public sealed record CreateTenantRequest(string Slug, string Name);

    /// <summary>Create a new tenant. The slug becomes the public path on the status page (e.g. <c>/status/{slug}</c>).</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost(Name = "createTenant")]
    [Idempotent]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Slug) || string.IsNullOrWhiteSpace(req.Name))
        {
            return JsonResults.UnprocessableEntity("Invalid tenant", "Slug and Name are required.").AsAction();
        }
        try
        {
            var b = await _boards.CreateAsync(req.Slug, req.Name, ct);
            return Created($"/api/admin/tenants/{b.BoardSlug}", b);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return JsonResults.Conflict("Slug already exists").AsAction();
        }
    }

    /// <summary>Fetch a tenant along with the IDs of every service it includes.</summary>
    [HttpGet("{slug}", Name = "getTenant")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var serviceIds = await _boards.GetServiceIdsAsync(b.BoardId, ct);
        return Ok(new { board = b, serviceIds });
    }

    public sealed record UpdateTenantRequest(string? Slug, string? Name, string? Host, IReadOnlyList<int>? ServiceIds);

    /// <summary>Rename a tenant, set its custom hostname, or replace its set of included services.</summary>
    /// <remarks>Send <c>Host</c> as an empty string to clear a previously assigned hostname.</remarks>
    [HttpPatch("{slug}", Name = "updateTenant")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Update(string slug, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var newSlug = req.Slug ?? b.BoardSlug;
        var newName = req.Name ?? b.BoardName;
        // Host semantics: null = leave unchanged, "" = clear, otherwise set.
        var newHost = req.Host is null ? b.BoardHost : NormalizeHost(req.Host);
        if (req.Host is not null && newHost is null && !string.IsNullOrWhiteSpace(req.Host))
        {
            return JsonResults.UnprocessableEntity("Invalid host", "Host must be a bare hostname such as status.example.com.").AsAction();
        }
        try
        {
            await _boards.UpdateAsync(b.BoardId, newSlug, newName, newHost, ct);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            var what = ex.ConstraintName == "ix_tenant_host" ? "Host" : "Slug";
            return JsonResults.Conflict($"{what} already exists").AsAction();
        }
        if (req.ServiceIds is not null)
        {
            await _boards.SetServicesAsync(b.BoardId, req.ServiceIds, ct);
        }
        return NoContent();
    }

    /// <summary>Set or clear the tenant's board theme.</summary>
    /// <remarks>The body is the whole theme object; it replaces the stored theme.
    /// Send JSON <c>null</c> to clear it and restore the default look.</remarks>
    [HttpPut("{slug}/theme", Name = "setTenantTheme")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> SetTheme(string slug, [FromBody] Newtonsoft.Json.Linq.JToken? theme, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var clear = theme is null || theme.Type == Newtonsoft.Json.Linq.JTokenType.Null;
        if (!clear && theme!.Type != Newtonsoft.Json.Linq.JTokenType.Object)
        {
            return JsonResults.UnprocessableEntity("Invalid theme", "Theme must be a JSON object or null.").AsAction();
        }
        var json = clear ? null : theme!.ToString(Newtonsoft.Json.Formatting.None);
        await _boards.UpdateThemeAsync(b.BoardId, json, ct);
        return NoContent();
    }

    /// <summary>Permanently delete a tenant. Subscribers and announcements scoped to it are deleted too.</summary>
    [HttpDelete("{slug}", Name = "deleteTenant")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var ok = await _boards.DeleteAsync(b.BoardId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>List confirmed and pending subscribers for a tenant.</summary>
    [HttpGet("{slug}/subscribers", Name = "listTenantSubscribers")]
    public async Task<IActionResult> Subscribers(string slug, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var rows = await _subscribers.ListAllForBoardAsync(b.BoardId, ct);
        return Ok(rows.Select(s => new { s.SubscriberId, s.SubscriberEmail, s.ConfirmedAt, s.CreatedAt }));
    }

    /// <summary>Remove a subscriber from a tenant. Admin-only; not the same as the public unsubscribe token flow.</summary>
    [HttpDelete("{slug}/subscribers/{subscriberId:int}", Name = "removeTenantSubscriber")]
    public async Task<IActionResult> RemoveSubscriber(string slug, int subscriberId, CancellationToken ct)
    {
        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();
        var ok = await _subscribers.AdminDeleteAsync(subscriberId, b.BoardId, ct);
        return ok ? NoContent() : NotFound();
    }

    public sealed record SubscribeRequest(string Email);

    /// <summary>Subscribe an email to a tenant's status updates. Sends a double-opt-in confirmation email.</summary>
    /// <remarks>Public anonymous endpoint. Rate-limited to 3 attempts per 10 minutes per IP. Honors <c>Idempotency-Key</c>.</remarks>
    [AllowAnonymous]
    // Public path; absolute route keeps it outside /api/admin where the rest of this controller lives.
    [HttpPost("/api/tenants/{slug}/subscribers", Name = "subscribe")]
    [Idempotent]
    [EnableRateLimiting(RateLimiting.SubscribePolicy)]
    [RequestSizeLimit(2 * 1024)]
    public async Task<IActionResult> Subscribe(string slug, [FromBody] SubscribeRequest req, CancellationToken ct)
    {
        if (!IsValidEmail(req.Email))
        {
            return JsonResults.UnprocessableEntity("Invalid email", "Email is not a valid address.").AsAction();
        }

        var b = await _boards.GetBySlugAsync(slug, ct);
        if (b is null) return NotFound();

        var maxPerTenant = _settings.MaxPerTenant;
        if (await _subscribers.CountForBoardAsync(b.BoardId, ct) >= maxPerTenant)
        {
            return JsonResults.TooManyRequests(
                title: "Subscriber cap reached",
                detail: "This board is not accepting new subscribers right now.",
                retryAfter: null).AsAction();
        }

        var (confirmToken, confirmHash) = SubscriberRepository.NewToken();
        var (unsubToken, unsubHash) = SubscriberRepository.NewToken();
        var sub = await _subscribers.CreateOrGetAsync(b.BoardId, req.Email, confirmHash, unsubHash, ct);
        if (sub is null)
        {
            return Accepted();
        }

        var publicUrl = (_config["Bump:Web:BaseUrl"] ?? "").TrimEnd('/');
        var confirmUrl = $"{publicUrl}/subscribe/confirm?token={confirmToken}";
        var unsubUrl = $"{publicUrl}/unsubscribe?token={unsubToken}";

        await _mail.SendAsync(SubscriberConfirm.Build(req.Email, b.BoardName, confirmUrl, unsubUrl), ct);
        return Accepted();
    }

    /// <summary>Lowercase and validate a custom hostname. Returns null for empty (clear) or invalid input.</summary>
    private static string? NormalizeHost(string host)
    {
        var h = host.Trim().ToLowerInvariant();
        if (h.Length == 0) return null;
        if (h.Length > 255) return null;
        // Bare hostname only: no scheme, port, path, or wildcard.
        return Uri.CheckHostName(h) == UriHostNameType.Dns ? h : null;
    }

    private static bool IsValidEmail(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 254) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(s);
            return string.Equals(addr.Address, s, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
