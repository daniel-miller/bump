// Bump API problem-report client for .NET Framework 4.8.
//
// Dependencies (NuGet):
//   - Newtonsoft.Json (>= 13.0.3)
//   - System.Net.Http     (in the BCL on 4.8; no package needed)
//
// Usage:
//
//   var client = new BumpProblemsClient(
//       baseUrl: "https://bump.example.com",
//       apiKey:  "<problems-bearer-key>");
//
//   try
//   {
//       DoWork();
//   }
//   catch (Exception ex)
//   {
//       long id = await client.SubmitAsync(new ProblemReportPayload
//       {
//           Type        = "https://example.com/problems/work-failed",
//           Title       = "Background job failed",
//           Status      = 500,
//           Detail      = ex.Message,
//           // RFC 9457: a URI ref identifying the specific occurrence. In a web
//           // app, the absolute URL of the failing request. In a CLI/job, a
//           // urn:uuid:... minted per run also works.
//           Instance    = HttpContext.Current?.Request?.Url?.AbsoluteUri,
//           Environment = "production",
//           Application = "my-app",
//           Exception   = ExceptionInfo.From(ex),
//           UserEmail   = currentUser?.Email
//       });
//   }
//
// Notes:
//   - The server is camelCase on the wire (Newtonsoft + CamelCasePropertyNamesContractResolver).
//     This client serializes with the same resolver, so PascalCase property names
//     here go out as camelCase on the wire and match the API contract.
//   - Server caps:
//       Body            64 KB        (Limits.ProblemBodyBytes)
//       Type            <= 512
//       Title           <= 512
//       Detail          <= 4096
//       Instance        <= 512
//       Environment     <= 64
//       Application     lowercase [a-z0-9-], <= 64 (slug)
//       Exception JSON  <= 32 KB
//       Extensions JSON <= 16 KB
//     Validate locally if you might exceed these — server rejects with 422.
//   - Set an Idempotency-Key (any stable string up to 255 chars) to make
//     retries safe: the server replays the original 201 if the body fingerprint
//     matches, and returns 422 if you reuse a key for a different body.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bump.Client
{
    public sealed class BumpProblemsClient : IDisposable
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly Uri _baseUri;
        private readonly string _apiKey;

        public BumpProblemsClient(string baseUrl, string apiKey, HttpClient httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey required", nameof(apiKey));

            // .NET 4.8 enables TLS 1.2 by default, but pin it for safety against
            // hosts that still allow the framework to negotiate downward.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            _baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            _apiKey = apiKey;
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttp = httpClient == null;
        }

        public Task<long> SubmitAsync(
            ProblemReportPayload payload,
            string idempotencyKey = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return SubmitInternalAsync(payload, idempotencyKey, cancellationToken);
        }

        private async Task<long> SubmitInternalAsync(
            ProblemReportPayload payload,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var url = new Uri(_baseUri, "api/problems");
            var json = JsonConvert.SerializeObject(payload, JsonSettings);

            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                {
                    var body = resp.Content == null
                        ? string.Empty
                        : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                        throw new BumpApiException((int)resp.StatusCode, body);

                    var created = JsonConvert.DeserializeObject<ProblemCreatedResponse>(body, JsonSettings);
                    if (created == null) throw new BumpApiException((int)resp.StatusCode, "Empty response body.");
                    return created.Id;
                }
            }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }

    public sealed class BumpApiException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }

        public BumpApiException(int statusCode, string responseBody)
            : base("Bump API returned HTTP " + statusCode + ": " + responseBody)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
        }
    }

    // Wire-format payload. Serializes camelCase on the wire.
    public sealed class ProblemReportPayload
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public int? Status { get; set; }
        public string Detail { get; set; }
        public string Instance { get; set; }
        public Dictionary<string, object> Extensions { get; set; }

        public string Environment { get; set; } = "";
        public ExceptionInfo Exception { get; set; }

        public string Application { get; set; } = "";

        public Guid? UserId { get; set; }
        public string UserEmail { get; set; }
    }

    public sealed class ExceptionInfo
    {
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public string StackTrace { get; set; }
        public List<ExceptionInfo> InnerExceptions { get; set; }

        // Snapshot a thrown Exception, recursing into InnerException and any
        // AggregateException.InnerExceptions. Mirrors the server's expected shape.
        public static ExceptionInfo From(Exception ex)
        {
            if (ex == null) return null;

            var info = new ExceptionInfo
            {
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                Value = ex.Message ?? "",
                StackTrace = ex.StackTrace
            };

            var children = new List<ExceptionInfo>();
            if (ex is AggregateException agg && agg.InnerExceptions != null)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    var child = From(inner);
                    if (child != null) children.Add(child);
                }
            }
            else if (ex.InnerException != null)
            {
                var child = From(ex.InnerException);
                if (child != null) children.Add(child);
            }

            if (children.Count > 0) info.InnerExceptions = children;
            return info;
        }
    }

    internal sealed class ProblemCreatedResponse
    {
        public long Id { get; set; }
    }
}
