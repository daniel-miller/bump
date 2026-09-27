using Bump.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Bump.Api.Controllers;

[ApiController]
[ProblemsAuthorize]
[BypassCsrf]
[EnableRateLimiting(RateLimiting.ProblemsPolicy)]
[Route("api/problems")]
[Tags("Problems")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class ProblemsController : ControllerBase
{
    // The parameters Query binds. Anything else is refused rather than ignored: ASP.NET Core drops
    // an unknown query parameter silently, so a misspelled or retired filter (from and to became
    // since and until) used to answer an unfiltered or empty list that looked like a real answer.
    private static readonly HashSet<string> QueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "environment", "appHandle", "fingerprint", "after", "before", "since", "until",
        "limit", "offset", "includeResolved",
    };

    private readonly ProblemRepository _repo;
    private readonly AppRepository _apps;
    private readonly EnvironmentRepository _environments;

    public ProblemsController(ProblemRepository repo, AppRepository apps, EnvironmentRepository environments)
    {
        _repo = repo;
        _apps = apps;
        _environments = environments;
    }

    /// <summary>Ingest a problem report. Payload is RFC 7807-style plus environment/app/user/exception metadata.</summary>
    /// <remarks>Requires the Problems Bearer key. Honors <c>Idempotency-Key</c>: resends return the original 201.</remarks>
    [HttpPost("", Name = ProblemsAuthFilter.ReportActionName)]
    [Idempotent]
    [RequestSizeLimit(Limits.ProblemBodyBytes)]
    [ProducesResponseType(typeof(ProblemCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] ProblemReportPayload payload)
    {
        var validationError = payload.Validate();
        if (validationError is not null) return validationError.AsAction();

        try
        {
            var id = await _repo.InsertAsync(payload);
            return JsonResults.Created($"/api/problems/{id}", new ProblemCreatedResponse(id)).AsAction();
        }
        catch (ProblemRepository.UnknownAppException ex)
        {
            return JsonResults.UnprocessableEntity(
                title: "Unknown app",
                detail: ex.Message).AsAction();
        }
        catch (ProblemRepository.UnknownEnvironmentException ex)
        {
            return JsonResults.UnprocessableEntity(
                title: "Unknown environment",
                detail: ex.Message).AsAction();
        }
    }

    /// <summary>Query stored problem reports with optional filters. Cap is 500 per page (limit clamped server-side).</summary>
    /// <remarks>Authenticated via either the Problems Bearer key or session cookie (admin).</remarks>
    /// <param name="environment">Environment handle or any of its aliases.</param>
    /// <param name="appHandle">App handle.</param>
    /// <param name="fingerprint">Problem fingerprint.</param>
    /// <param name="after">Reported strictly after this time (&gt;). A value with no offset is read as UTC.</param>
    /// <param name="before">Reported strictly before this time (&lt;). A value with no offset is read as UTC.</param>
    /// <param name="since">Reported at or after this time (&gt;=). A value with no offset is read as UTC.</param>
    /// <param name="until">Reported at or before this time (&lt;=). A value with no offset is read as UTC.</param>
    /// <param name="limit">Page size, clamped to 1-500.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="includeResolved">Include resolved problems. Off by default, so a resolved problem
    /// the admin UI can show is left out of this list unless this is true.</param>
    /// <response code="400">An unknown query parameter, or an app or environment Bump does not know.
    /// A filter that cannot be honoured is an error, never an empty list.</response>
    [HttpGet("", Name = "listProblems")]
    [ProducesResponseType(typeof(IEnumerable<ProblemReportRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Query(
        [FromQuery] string? environment,
        [FromQuery] string? appHandle,
        [FromQuery] string? fingerprint,
        [FromQuery] DateTime? after,
        [FromQuery] DateTime? before,
        [FromQuery] DateTime? since,
        [FromQuery] DateTime? until,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] bool includeResolved = false)
    {
        var unknown = Request.Query.Keys.Where(k => !QueryParameters.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            return JsonResults.BadRequest(
                title: "Unknown query parameter",
                detail: $"GET /api/problems does not accept {string.Join(", ", unknown)}. "
                    + $"It accepts {string.Join(", ", QueryParameters)}. The time filters are after, before, since and until.")
                .AsAction();
        }

        if (appHandle is not null && await _apps.GetByHandleAsync(appHandle) is null)
        {
            return JsonResults.BadRequest(
                title: "Unknown app",
                detail: $"No app is registered with handle '{appHandle}'. Handles are matched exactly.").AsAction();
        }

        if (environment is not null && !await IsKnownEnvironmentAsync(environment))
        {
            return JsonResults.BadRequest(
                title: "Unknown environment",
                detail: $"No environment has the handle or alias '{environment}'.").AsAction();
        }

        var filter = new ProblemReportFilter
        {
            Environment = environment,
            AppHandle = appHandle,
            Fingerprint = fingerprint,
            After = ProblemReportFilter.AsUtc(after),
            Before = ProblemReportFilter.AsUtc(before),
            Since = ProblemReportFilter.AsUtc(since),
            Until = ProblemReportFilter.AsUtc(until),
            Limit = Math.Clamp(limit, 1, 500),
            Offset = Math.Max(offset, 0),
            IncludeResolved = includeResolved,
        };
        var results = await _repo.QueryAsync(filter);
        return JsonResults.Ok(results).AsAction();
    }

    /// <summary>Fetch a single problem report. Returns JSON by default; send <c>Accept: text/markdown</c> for a Markdown rendering suitable for pasting into a bug tracker.</summary>
    [HttpGet("{id:long}", Name = "getProblem")]
    [Produces("application/json", "text/markdown")]
    [ProducesResponseType(typeof(ProblemReportRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id)
    {
        var record = await _repo.GetByIdAsync(id);
        if (record is null)
            return JsonResults.NotFound("Problem not found", $"No problem with id '{id}'.").AsAction();

        if (WantsMarkdown(Request.Headers.Accept))
        {
            return Content(ProblemMarkdown.Render(record), "text/markdown; charset=utf-8");
        }

        return JsonResults.Ok(record).AsAction();
    }

    /// <summary>Mark a problem as resolved (sets <c>resolved_at</c> to the current time).</summary>
    /// <remarks>Authenticated via either the Problems Bearer key or session cookie (admin).</remarks>
    [HttpPost("{id:long}/resolve", Name = "resolveProblem")]
    [ProducesResponseType(typeof(ProblemReportRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Resolve(long id) => SetResolvedCore(id, true);

    /// <summary>Mark a problem as unresolved (clears <c>resolved_at</c>).</summary>
    /// <remarks>Authenticated via either the Problems Bearer key or session cookie (admin).</remarks>
    [HttpPost("{id:long}/unresolve", Name = "unresolveProblem")]
    [ProducesResponseType(typeof(ProblemReportRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Unresolve(long id) => SetResolvedCore(id, false);

    private async Task<IActionResult> SetResolvedCore(long id, bool resolved)
    {
        var updated = await _repo.SetResolvedAsync(id, resolved);
        if (!updated)
            return JsonResults.NotFound("Problem not found", $"No problem with id '{id}'.").AsAction();
        var record = await _repo.GetByIdAsync(id);
        return JsonResults.Ok(record).AsAction();
    }

    /// <summary>Permanently delete a stored problem report by id.</summary>
    /// <remarks>Authenticated via either the Problems Bearer key or session cookie (admin).</remarks>
    [HttpDelete("{id:long}", Name = "deleteProblem")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id)
    {
        var deleted = await _repo.DeleteAsync(id);
        return deleted
            ? NoContent()
            : JsonResults.NotFound("Problem not found", $"No problem with id '{id}'.").AsAction();
    }

    /// <summary>Permanently delete a batch of stored problem reports by id. Cap is 500 keys per request.</summary>
    /// <remarks>
    /// POST rather than DELETE-with-body, since intermediaries are free to strip a body from a DELETE.
    /// The whole batch is deleted in one statement. Keys that no longer exist are ignored, so a
    /// double-submit still returns 200 with a lower <c>deleted</c> count rather than a 404.
    /// Authenticated via either the Problems Bearer key or session cookie (admin).
    /// </remarks>
    [HttpPost("delete", Name = "deleteProblems")]
    [RequestSizeLimit(Limits.ProblemBodyBytes)]
    [ProducesResponseType(typeof(ProblemsDeletedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMany([FromBody] ProblemBulkDeleteRequest request)
    {
        var validationError = request.Validate();
        if (validationError is not null) return validationError.AsAction();

        var deleted = await _repo.DeleteManyAsync(request.ProblemKeys, HttpContext.RequestAborted);
        return JsonResults.Ok(new ProblemsDeletedResponse(deleted)).AsAction();
    }

    // The same match the query makes: the handle or any alias.
    private async Task<bool> IsKnownEnvironmentAsync(string environment)
    {
        var all = await _environments.GetAllAsync();
        return all.Any(e => e.EnvironmentHandle == environment || e.EnvironmentAliases.Contains(environment));
    }

    private static bool WantsMarkdown(Microsoft.Extensions.Primitives.StringValues accept)
    {
        foreach (var value in accept)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (var part in value.Split(','))
            {
                var media = part.Split(';', 2)[0].Trim();
                if (media.Equals("text/markdown", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
