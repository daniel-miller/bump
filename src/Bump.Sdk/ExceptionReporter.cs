using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bump.Sdk;

public class ExceptionReporter : IDisposable
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc
    };

    private readonly HttpClient _http;
    private readonly BumpOptions _options;
    private readonly ILogger _logger;

    public ExceptionReporter(BumpOptions options, HttpClient? httpClient = null, ILogger<ExceptionReporter>? logger = null)
    {
        _options = options;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        _logger = logger ?? NullLogger<ExceptionReporter>.Instance;
    }

    public async Task CaptureAsync(
        Exception ex,
        UserContext? user = null,
        Dictionary<string, object>? extensions = null,
        string? instance = null)
    {
        var typeName = ex.GetType().FullName ?? ex.GetType().Name;
        var type = string.IsNullOrEmpty(_options.ProblemTypePrefix)
            ? typeName
            : _options.ProblemTypePrefix.TrimEnd('/') + "/" + typeName;

        var payload = new
        {
            Type = type,
            Title = ex.GetType().Name,
            Status = _options.DefaultStatus,
            Detail = ex.Message,
            Instance = instance,
            Extensions = extensions,
            Environment = _options.Environment,
            Application = _options.AppSlug,
            Exception = ExceptionInfo.From(ex),
            UserId = user?.Id,
            UserEmail = user?.Email
        };

        try
        {
            var json = JsonConvert.SerializeObject(payload, JsonSettings);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("api/problems", content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Bump rejected problem report: {Status} {Body}", (int)response.StatusCode, body);
            }
        }
        catch (Exception reportingEx)
        {
            // Reporting must never throw, but a logged warning helps when callers
            // wonder why their dashboard is empty.
            _logger.LogWarning(reportingEx, "Failed to send problem report to Bump.");
        }
    }

    public void Dispose() => _http.Dispose();
}
