using System.Net.Http.Headers;
using System.Text;
using Polly;
using Polly.Retry;

namespace Bump.Api.Mail;

public sealed class MailgunOptions
{
    public string ApiKey { get; init; } = "";
    public string Domain { get; init; } = "";
    public string From { get; init; } = "";
    public string Region { get; init; } = "us";
}

public sealed class MailgunClient : IMailgunClient
{
    private static readonly AsyncRetryPolicy<HttpResponseMessage> Retry =
        Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    private readonly HttpClient _http;
    private readonly MailgunOptions _options;
    private readonly ILogger<MailgunClient> _logger;

    public MailgunClient(HttpClient http, MailgunOptions options, ILogger<MailgunClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        if (string.IsNullOrEmpty(options.ApiKey) || string.IsNullOrEmpty(options.Domain))
        {
            _logger.LogWarning("Mailgun is not configured (ApiKey/Domain). Outbound email will fail.");
        }

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{options.ApiKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task SendAsync(MailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey) || string.IsNullOrEmpty(_options.Domain))
        {
            _logger.LogInformation("Skipping email (Mailgun not configured): subject={Subject} to={To}",
                message.Subject, string.Join(",", message.To));
            return;
        }

        var endpoint = _options.Region.Equals("eu", StringComparison.OrdinalIgnoreCase)
            ? $"https://api.eu.mailgun.net/v3/{_options.Domain}/messages"
            : $"https://api.mailgun.net/v3/{_options.Domain}/messages";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message.From ?? _options.From), "from");
        foreach (var to in message.To)
        {
            content.Add(new StringContent(to), "to");
        }
        content.Add(new StringContent(message.Subject), "subject");
        content.Add(new StringContent(message.Text), "text");
        if (message.Html is not null)
        {
            content.Add(new StringContent(message.Html), "html");
        }
        foreach (var tag in message.Tags)
        {
            content.Add(new StringContent(tag), "o:tag");
        }

        var resp = await Retry.ExecuteAsync(async _ =>
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            return await _http.SendAsync(msg, ct);
        }, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Mailgun rejected message: {Status} {Body}", (int)resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }
    }
}
