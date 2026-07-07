using Newtonsoft.Json;

namespace Bump.Api.Services;

/// <summary>
/// Cloudflare Turnstile verifier. Reads
/// <c>Bump:Captcha:TurnstileSecret</c>; if unset, verification is
/// skipped and a warning is logged on startup. The siteverify endpoint
/// is unauthenticated for the secret holder, so we POST the user's
/// token plus our secret and parse the boolean <c>success</c>.
/// </summary>
public sealed class CaptchaVerifier
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CaptchaVerifier> _logger;
    private readonly string _secret;

    public CaptchaVerifier(IHttpClientFactory httpFactory, IConfiguration config, ILogger<CaptchaVerifier> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _secret = config["Bump:Captcha:TurnstileSecret"] ?? string.Empty;
        if (string.IsNullOrEmpty(_secret))
        {
            _logger.LogWarning(
                "Bump:Captcha:TurnstileSecret is not configured. CAPTCHA enforcement is disabled — set the secret before exposing public sign-up endpoints.");
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_secret);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            // Dev posture: allow but warned at startup. In production the
            // secret must be set; if it isn't, this method short-circuits
            // and the only line of defense is the rate limiter.
            return true;
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var http = _httpFactory.CreateClient(nameof(CaptchaVerifier));
        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", _secret),
            new("response", token!)
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form.Add(new("remoteip", remoteIp!));
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync(VerifyUrl, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile verify HTTP {Status}.", (int)resp.StatusCode);
                return false;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonConvert.DeserializeObject<TurnstileResponse>(body);
            return parsed is not null && parsed.success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile verify failed.");
            return false;
        }
    }

    private sealed class TurnstileResponse
    {
        public bool success { get; set; }
    }
}
