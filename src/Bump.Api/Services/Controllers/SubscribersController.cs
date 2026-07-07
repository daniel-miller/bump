using Microsoft.AspNetCore.Mvc;

namespace Bump.Api.Services.Controllers;

[ApiController]
[Route("api/subscribers")]
[Tags("Subscribers")]
public sealed class SubscribersController : ControllerBase
{
    private readonly SubscriberRepository _subscribers;
    private readonly IConfiguration _config;

    public SubscribersController(SubscriberRepository subscribers, IConfiguration config)
    {
        _subscribers = subscribers;
        _config = config;
    }

    /// <summary>Confirm a pending subscription via the link emailed to the subscriber. Redirects to the public confirmation page on success.</summary>
    [HttpGet("confirm", Name = "confirmSubscription")]
    public async Task<IActionResult> Confirm([FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var hash = SubscriberRepository.HashToken(token);
        var sub = await _subscribers.ConfirmAsync(hash, ct);
        if (sub is null) return NotFound();
        var publicUrl = (_config["Bump:Hosting:PublicBaseUrl"] ?? "").TrimEnd('/');
        return Redirect($"{publicUrl}/subscribe/confirmed");
    }

    /// <summary>Unsubscribe via the one-shot token included in every notification email.</summary>
    [HttpPost("unsubscribe", Name = "unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var hash = SubscriberRepository.HashToken(token);
        var ok = await _subscribers.UnsubscribeAsync(hash, ct);
        return ok ? NoContent() : NotFound();
    }
}
