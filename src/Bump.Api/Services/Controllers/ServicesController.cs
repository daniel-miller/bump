using Bump.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/admin/services")]
[Tags("Services")]
[Authorize(AuthenticationSchemes = SessionAuthHandler.SchemeName, Roles = "admin")]
public sealed class ServicesController : ControllerBase
{
    private readonly ServiceRepository _services;
    private readonly ITimezoneResolver _tz;

    public ServicesController(ServiceRepository services, ITimezoneResolver tz)
    {
        _services = services;
        _tz = tz;
    }

    public sealed record ServiceDto(
        int ServiceId, string Slug, string Name, string Url, string Tenant, string Environment, bool Paused, bool IsPrivate,
        string LastStatus, int LatencyMs, decimal Uptime,
        IReadOnlyList<string> History, DateTimeOffset? LastCheckAt, DateTimeOffset? LastOutageAt,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    /// <summary>List every monitored service with its current status, uptime, and recent latency.</summary>
    [HttpGet(Name = "listServices")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var services = await _services.ListAsync(ct);
        var ids = services.Select(m => m.ServiceId).ToList();
        var states = await _services.GetStatesAsync(ids, ct);
        var dto = services.Select(m =>
        {
            states.TryGetValue(m.ServiceId, out var s);
            return new ServiceDto(
                m.ServiceId, m.ServiceSlug, m.ServiceName, m.ServiceUrl, m.ServiceTenant, m.ServiceEnvironment, m.ServicePaused, m.IsPrivate,
                s?.LastStatus ?? ServiceStatuses.Operational, s?.LatencyMs ?? 0, s?.UptimePct ?? 100m,
                s?.History ?? new List<string>(), s?.LastCheckAt, s?.LastOutageAt,
                m.CreatedAt, m.UpdatedAt);
        }).ToList();
        return Ok(dto);
    }

    public sealed record CreateServiceRequest(string Slug, string Name, string Url, string Tenant, string Environment);

    /// <summary>Register a new service to monitor. The URL must resolve to a non-private address (RFC1918, link-local, and loopback are rejected).</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost(Name = "createService")]
    [Idempotent]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequest req, CancellationToken ct)
    {
        var slugErr = ServiceSlugRules.ValidateSlug(req.Slug);
        if (slugErr is not null) return slugErr.AsAction();
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
        {
            return JsonResults.UnprocessableEntity("Invalid name", "Name is required and must be 100 characters or fewer.").AsAction();
        }
        if (!ProbeAddressGuard.TryValidateUrl(req.Url, out _, out var urlErr))
        {
            return JsonResults.UnprocessableEntity("Invalid URL", urlErr!).AsAction();
        }
        var tenantErr = ServiceSlugRules.ValidateTagSlug(req.Tenant, "tenant");
        if (tenantErr is not null) return tenantErr.AsAction();
        var envErr = ServiceSlugRules.ValidateTagSlug(req.Environment, "environment");
        if (envErr is not null) return envErr.AsAction();

        try
        {
            var m = await _services.CreateAsync(req.Slug, req.Name, req.Url, req.Tenant, req.Environment, ct);
            return Created($"/api/admin/services/{m.ServiceSlug}", new { slug = m.ServiceSlug });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return JsonResults.Conflict("Slug already exists").AsAction();
        }
    }

    /// <summary>Fetch a single service with its current state.</summary>
    [HttpGet("{slug}", Name = "getService")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var m = await _services.GetBySlugAsync(slug, ct);
        if (m is null) return NotFound();
        var s = await _services.GetStateAsync(m.ServiceId, ct);
        return Ok(new ServiceDto(
            m.ServiceId, m.ServiceSlug, m.ServiceName, m.ServiceUrl, m.ServiceTenant, m.ServiceEnvironment, m.ServicePaused, m.IsPrivate,
            s?.LastStatus ?? ServiceStatuses.Operational, s?.LatencyMs ?? 0, s?.UptimePct ?? 100m,
            s?.History ?? new List<string>(), s?.LastCheckAt, s?.LastOutageAt,
            m.CreatedAt, m.UpdatedAt));
    }

    public sealed record UpdateServiceRequest(string? Name, string? Url, string? Tenant, string? Environment, bool? IsPrivate);

    /// <summary>Update service metadata. Omitted fields are left unchanged.</summary>
    [HttpPatch("{slug}", Name = "updateService")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Update(string slug, [FromBody] UpdateServiceRequest req, CancellationToken ct)
    {
        var m = await _services.GetBySlugAsync(slug, ct);
        if (m is null) return NotFound();

        var name = req.Name ?? m.ServiceName;
        var url = req.Url ?? m.ServiceUrl;
        var tenant = req.Tenant ?? m.ServiceTenant;
        var environment = req.Environment ?? m.ServiceEnvironment;
        var isPrivate = req.IsPrivate ?? m.IsPrivate;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return JsonResults.UnprocessableEntity("Invalid name", "Name must be 1-100 characters.").AsAction();
        }
        if (!ProbeAddressGuard.TryValidateUrl(url, out _, out var urlErr))
        {
            return JsonResults.UnprocessableEntity("Invalid URL", urlErr!).AsAction();
        }
        var tenantErr = ServiceSlugRules.ValidateTagSlug(tenant, "tenant");
        if (tenantErr is not null) return tenantErr.AsAction();
        var envErr = ServiceSlugRules.ValidateTagSlug(environment, "environment");
        if (envErr is not null) return envErr.AsAction();

        await _services.UpdateAsync(slug, name, url, tenant, environment, isPrivate, ct);
        return NoContent();
    }

    /// <summary>Permanently delete a service.</summary>
    [HttpDelete("{slug}", Name = "deleteService")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        var ok = await _services.DeleteAsync(slug, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Pause probes for this service. New probes will not run until resumed.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost("{slug}/pause", Name = "pauseService")]
    [Idempotent]
    public async Task<IActionResult> Pause(string slug, CancellationToken ct)
    {
        await _services.SetPausedAsync(slug, true, ct);
        return NoContent();
    }

    /// <summary>Resume probes for a previously paused service.</summary>
    /// <remarks>Honors <c>Idempotency-Key</c>.</remarks>
    [HttpPost("{slug}/resume", Name = "resumeService")]
    [Idempotent]
    public async Task<IActionResult> Resume(string slug, CancellationToken ct)
    {
        await _services.SetPausedAsync(slug, false, ct);
        return NoContent();
    }

    /// <summary>Daily uptime percentages for charting. <c>range</c> may be <c>24h</c>, <c>7d</c> (default), or <c>30d</c>.</summary>
    [HttpGet("{slug}/uptime", Name = "getServiceUptime")]
    public async Task<IActionResult> Uptime(string slug, [FromQuery] string range = "7d", CancellationToken ct = default)
    {
        var m = await _services.GetBySlugAsync(slug, ct);
        if (m is null) return NotFound();
        int days = ParseRange(range);
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var dailies = await _services.GetDailyAsync(m.ServiceId, days, tz, ct);
        var points = dailies.Select(d => new
        {
            day = d.Day.ToString("yyyy-MM-dd"),
            probes = d.Probes,
            uptime = d.Probes == 0 ? 100m : Math.Round((decimal)d.OperationalCount / d.Probes * 100m, 2)
        }).ToList();
        return Ok(new { range, points });
    }

    /// <summary>Daily average latency in milliseconds for charting. <c>range</c> may be <c>24h</c>, <c>7d</c> (default), or <c>30d</c>.</summary>
    [HttpGet("{slug}/latency", Name = "getServiceLatency")]
    public async Task<IActionResult> Latency(string slug, [FromQuery] string range = "7d", CancellationToken ct = default)
    {
        var m = await _services.GetBySlugAsync(slug, ct);
        if (m is null) return NotFound();
        int days = ParseRange(range);
        var tz = await _tz.ResolveAsync(HttpContext.User, ct);
        var dailies = await _services.GetDailyAsync(m.ServiceId, days, tz, ct);
        var points = dailies.Select(d => new
        {
            day = d.Day.ToString("yyyy-MM-dd"),
            probes = d.Probes,
            latencyMs = d.Probes == 0 ? 0 : (int)(d.LatencySumMs / d.Probes)
        }).ToList();
        return Ok(new { range, points });
    }

    private static int ParseRange(string range) => range switch { "24h" => 1, "30d" => 30, _ => 7 };
}
