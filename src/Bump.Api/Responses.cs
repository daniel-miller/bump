namespace Bump.Api;

/// <summary>
/// Response body for <c>POST /api/problems</c>. A dedicated type (rather
/// than an anonymous object) so the generated OpenAPI spec can describe
/// it to consumers generating clients.
/// </summary>
public sealed record ProblemCreatedResponse(long Id);

/// <summary>
/// Response body for <c>POST /api/problems/delete</c>. <c>Deleted</c> is the
/// number of rows actually removed, which is lower than the number of keys
/// submitted when some were already gone.
/// </summary>
public sealed record ProblemsDeletedResponse(int Deleted);

/// <summary>
/// Response body for <c>GET /api/health</c>. Values are always the string
/// literals <c>"healthy"</c> or <c>"unhealthy"</c>.
/// </summary>
public sealed record HealthResponse(string Status);
