using Bump.Api.Auth;
using Bump.Api.Mail;
using Bump.Api.Mail.MailTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/admin/owners")]
[Tags("Owners")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class OwnersController : ControllerBase
{
    private readonly OwnerRepository _owners;
    private readonly SubscriberRepository _subscribers;
    private readonly IMailgunClient _mail;
    private readonly IConfiguration _config;
    private readonly SubscribersSettings _settings;

    public OwnersController(
        OwnerRepository owners,
        SubscriberRepository subscribers,
        IMailgunClient mail,
        IConfiguration config,
        SubscribersSettings settings)
    {
        _owners = owners;
        _subscribers = subscribers;
        _mail = mail;
        _config = config;
        _settings = settings;
    }

    /// <summary>List every owner (status board grouping), in infra roster order.</summary>
    [HttpGet(Name = "listOwners")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _owners.ListAsync(ct);
        return Ok(rows);
    }

    public sealed record CreateOwnerRequest(string Handle, string Name);

    /// <summary>Create a new owner. The handle becomes the public board path on the status page (e.g. <c>/boards/{handle}</c>).</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost(Name = "createOwner")]
    [Idempotent]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateOwnerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Handle) || string.IsNullOrWhiteSpace(req.Name))
        {
            return JsonResults.UnprocessableEntity("Invalid owner", "Handle and Name are required.").AsAction();
        }
        try
        {
            var o = await _owners.CreateAsync(req.Handle, req.Name, ct);
            return Created($"/api/admin/owners/{o.OwnerHandle}", o);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return JsonResults.Conflict("Handle already exists").AsAction();
        }
    }

    /// <summary>Fetch an owner along with the IDs of every service it includes.</summary>
    [HttpGet("{handle}", Name = "getOwner")]
    public async Task<IActionResult> Get(string handle, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var serviceIds = await _owners.GetServiceIdsAsync(o.OwnerId, ct);
        return Ok(new { owner = o, serviceIds });
    }

    public sealed record UpdateOwnerRequest(string? Handle, string? Name, string? Host, IReadOnlyList<int>? ServiceIds);

    /// <summary>Rename an owner, set its custom hostname, or replace its set of included services.</summary>
    /// <remarks>Send <c>Host</c> as an empty string to clear a previously assigned hostname.</remarks>
    [HttpPatch("{handle}", Name = "updateOwner")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Update(string handle, [FromBody] UpdateOwnerRequest req, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var newHandle = req.Handle ?? o.OwnerHandle;
        var newName = req.Name ?? o.OwnerName;
        // Host semantics: null = leave unchanged, "" = clear, otherwise set.
        var newHost = req.Host is null ? o.OwnerHost : NormalizeHost(req.Host);
        if (req.Host is not null && newHost is null && !string.IsNullOrWhiteSpace(req.Host))
        {
            return JsonResults.UnprocessableEntity("Invalid host", "Host must be a bare hostname such as status.example.com.").AsAction();
        }
        try
        {
            await _owners.UpdateAsync(o.OwnerId, newHandle, newName, newHost, ct);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            var what = ex.ConstraintName == "ix_owner_host" ? "Host" : "Handle";
            return JsonResults.Conflict($"{what} already exists").AsAction();
        }
        if (req.ServiceIds is not null)
        {
            await _owners.SetServicesAsync(o.OwnerId, req.ServiceIds, ct);
        }
        return NoContent();
    }

    /// <summary>Set or clear the owner's board theme.</summary>
    /// <remarks>The body is the whole theme object; it replaces the stored theme.
    /// Send JSON <c>null</c> to clear it and restore the default look.</remarks>
    [HttpPut("{handle}/theme", Name = "setOwnerTheme")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> SetTheme(string handle, [FromBody] Newtonsoft.Json.Linq.JToken? theme, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var clear = theme is null || theme.Type == Newtonsoft.Json.Linq.JTokenType.Null;
        if (!clear && theme!.Type != Newtonsoft.Json.Linq.JTokenType.Object)
        {
            return JsonResults.UnprocessableEntity("Invalid theme", "Theme must be a JSON object or null.").AsAction();
        }
        var json = clear ? null : theme!.ToString(Newtonsoft.Json.Formatting.None);
        await _owners.UpdateThemeAsync(o.OwnerId, json, ct);
        return NoContent();
    }

    /// <summary>Permanently delete an owner. Subscribers and announcements scoped to it are deleted too. Refused with 409 while any app still belongs to the owner.</summary>
    [HttpDelete("{handle}", Name = "deleteOwner")]
    public async Task<IActionResult> Delete(string handle, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        try
        {
            var ok = await _owners.DeleteAsync(o.OwnerId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is Npgsql.PostgresErrorCodes.RestrictViolation or Npgsql.PostgresErrorCodes.ForeignKeyViolation)
        {
            return JsonResults.Conflict(
                title: "Owner has apps",
                detail: $"Apps still belong to '{handle}'. Move them to another owner before deleting it.").AsAction();
        }
    }

    /// <summary>List confirmed and pending subscribers for an owner.</summary>
    [HttpGet("{handle}/subscribers", Name = "listOwnerSubscribers")]
    public async Task<IActionResult> Subscribers(string handle, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var rows = await _subscribers.ListAllForOwnerAsync(o.OwnerId, ct);
        return Ok(rows.Select(s => new { s.SubscriberId, s.SubscriberEmail, s.ConfirmedAt, s.CreatedAt }));
    }

    public sealed record SubscriberEmailRequest(string Email);

    /// <summary>Add a subscriber to an owner. Admin-only. The operator vouches for the address, so it is subscribed at once, and the subscriber is emailed that they were added, with the board link and an unsubscribe link.</summary>
    /// <remarks>A pending sign-up for the same address is confirmed in place. 409 when the address is already subscribed.</remarks>
    [HttpPost("{handle}/subscribers", Name = "addOwnerSubscriber")]
    [RequestSizeLimit(2 * 1024)]
    public async Task<IActionResult> AddSubscriber(string handle, [FromBody] SubscriberEmailRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
            return JsonResults.UnprocessableEntity("Invalid email", "Email is not a valid address.").AsAction();
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        if (await _subscribers.CountForOwnerAsync(o.OwnerId, ct) >= _settings.MaxPerOwner)
        {
            return JsonResults.Conflict(
                "Subscriber cap reached",
                $"{o.OwnerName} already has the maximum of {_settings.MaxPerOwner} subscribers.").AsAction();
        }

        var (_, confirmHash) = SubscriberRepository.NewToken();
        var (_, unsubRaw) = SubscriberRepository.NewUnsubscribeToken();
        var sub = await _subscribers.AdminAddAsync(o.OwnerId, email, confirmHash, unsubRaw, ct);
        if (sub is null)
            return JsonResults.Conflict("Already subscribed", $"{email} is already subscribed to {o.OwnerName}.").AsAction();

        await SendAddedNoticeAsync(o, sub, ct);
        return Created($"/api/admin/owners/{o.OwnerHandle}/subscribers/{sub.SubscriberId}", SubscriberView(sub));
    }

    /// <summary>Change a subscriber's address. Admin-only. The new address is subscribed at once and emailed that it was added.</summary>
    /// <remarks>409 when another subscriber of the same owner already has the address.</remarks>
    [HttpPatch("{handle}/subscribers/{subscriberId:int}", Name = "updateOwnerSubscriber")]
    [RequestSizeLimit(2 * 1024)]
    public async Task<IActionResult> UpdateSubscriber(string handle, int subscriberId, [FromBody] SubscriberEmailRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
            return JsonResults.UnprocessableEntity("Invalid email", "Email is not a valid address.").AsAction();
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var current = await _subscribers.GetAsync(subscriberId, o.OwnerId, ct);
        if (current is null) return NotFound();

        // An unchanged address is not a new subscription: no email, no new token.
        if (string.Equals(current.SubscriberEmail, email, StringComparison.OrdinalIgnoreCase))
            return Ok(SubscriberView(current));

        try
        {
            var (_, unsubRaw) = SubscriberRepository.NewUnsubscribeToken();
            var sub = await _subscribers.AdminUpdateEmailAsync(subscriberId, o.OwnerId, email, unsubRaw, ct);
            if (sub is null) return NotFound();
            await SendAddedNoticeAsync(o, sub, ct);
            return Ok(SubscriberView(sub));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            return JsonResults.Conflict("Already subscribed", $"{email} is already subscribed to {o.OwnerName}.").AsAction();
        }
    }

    private static object SubscriberView(Subscriber s) =>
        new { s.SubscriberId, s.SubscriberEmail, s.ConfirmedAt, s.CreatedAt };

    // Someone an operator subscribes hears about it from Bump, with a way out, so no
    // one receives outage emails without knowing why.
    private async Task SendAddedNoticeAsync(Owner o, Subscriber s, CancellationToken ct)
    {
        var publicUrl = (_config["Bump:Web:BaseUrl"] ?? "").TrimEnd('/');
        var boardUrl = string.IsNullOrWhiteSpace(o.OwnerHost) ? $"{publicUrl}/boards/{o.OwnerHandle}" : $"https://{o.OwnerHost}";
        var unsubUrl = $"{publicUrl}/unsubscribe?token={SubscriberRepository.EncodeToken(s.UnsubscribeToken)}";
        await _mail.SendAsync(SubscriberAdded.Build(s.SubscriberEmail, o.OwnerName, boardUrl, unsubUrl), ct);
    }

    /// <summary>Remove a subscriber from an owner. Admin-only; not the same as the public unsubscribe token flow.</summary>
    [HttpDelete("{handle}/subscribers/{subscriberId:int}", Name = "removeOwnerSubscriber")]
    public async Task<IActionResult> RemoveSubscriber(string handle, int subscriberId, CancellationToken ct)
    {
        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();
        var ok = await _subscribers.AdminDeleteAsync(subscriberId, o.OwnerId, ct);
        return ok ? NoContent() : NotFound();
    }

    public sealed record SubscribeRequest(string Email);

    /// <summary>Subscribe an email to an owner's status updates. Sends a double-opt-in confirmation email.</summary>
    /// <remarks>Public anonymous endpoint. Rate-limited to 3 attempts per 10 minutes per IP. Honors <c>Idempotency-Key</c>.</remarks>
    [AllowAnonymous]
    // Public path; absolute route keeps it outside /api/admin where the rest of this controller lives.
    [HttpPost("/api/owners/{handle}/subscribers", Name = "subscribe")]
    [Idempotent]
    [EnableRateLimiting(RateLimiting.SubscribePolicy)]
    [RequestSizeLimit(2 * 1024)]
    public async Task<IActionResult> Subscribe(string handle, [FromBody] SubscribeRequest req, CancellationToken ct)
    {
        if (!IsValidEmail(req.Email))
        {
            return JsonResults.UnprocessableEntity("Invalid email", "Email is not a valid address.").AsAction();
        }

        var o = await _owners.GetByHandleAsync(handle, ct);
        if (o is null) return NotFound();

        var maxPerOwner = _settings.MaxPerOwner;
        if (await _subscribers.CountForOwnerAsync(o.OwnerId, ct) >= maxPerOwner)
        {
            return JsonResults.TooManyRequests(
                title: "Subscriber cap reached",
                detail: "This board is not accepting new subscribers right now.",
                retryAfter: null).AsAction();
        }

        var (confirmToken, confirmHash) = SubscriberRepository.NewToken();
        var (unsubToken, unsubRaw) = SubscriberRepository.NewUnsubscribeToken();
        var sub = await _subscribers.CreateOrGetAsync(o.OwnerId, req.Email, confirmHash, unsubRaw, ct);
        if (sub is null)
        {
            return Accepted();
        }

        var publicUrl = (_config["Bump:Web:BaseUrl"] ?? "").TrimEnd('/');
        var confirmUrl = $"{publicUrl}/subscribe/confirm?token={confirmToken}";
        var unsubUrl = $"{publicUrl}/unsubscribe?token={unsubToken}";

        await _mail.SendAsync(SubscriberConfirm.Build(req.Email, o.OwnerName, confirmUrl, unsubUrl), ct);
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
