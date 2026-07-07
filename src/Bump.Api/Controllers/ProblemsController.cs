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
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public sealed class ProblemsController : ControllerBase
{
    private readonly ProblemRepository _repo;

    public ProblemsController(ProblemRepository repo)
    {
        _repo = repo;
    }

    /// <summary>Ingest a problem report. Payload is RFC 7807-style plus environment/app/user/exception metadata.</summary>
    /// <remarks>Requires the Problems Bearer key. Honors <c>Idempotency-Key</c>: resends return the original 201.</remarks>
    [HttpPost("", Name = "createProblem")]
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
    [HttpGet("", Name = "listProblems")]
    [ProducesResponseType(typeof(IEnumerable<ProblemReportRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Query(
        [FromQuery] string? environment,
        [FromQuery] string? appSlug,
        [FromQuery] string? fingerprint,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] bool includeResolved = false)
    {
        var filter = new ProblemReportFilter
        {
            Environment = environment,
            AppSlug = appSlug,
            Fingerprint = fingerprint,
            From = from,
            To = to,
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
