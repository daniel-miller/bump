using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bump.Api;

/// <summary>
/// Swashbuckle operation filter that adds the <c>Idempotency-Key</c>
/// header parameter to any controller action carrying
/// <see cref="IdempotentAttribute"/>.
/// </summary>
public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasMarker =
            context.MethodInfo?.GetCustomAttributes(typeof(IdempotentAttribute), inherit: true).Length > 0
            || (context.ApiDescription.ActionDescriptor is ControllerActionDescriptor cad
                && cad.ControllerTypeInfo.GetCustomAttributes(typeof(IdempotentAttribute), inherit: true).Length > 0);

        if (!hasMarker) return;

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Description =
                "Optional. Retry-safe request identifier (up to 255 characters). " +
                "Sending the same key with the same body replays the original " +
                "response. Reusing a key with a different body returns 422.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                MaxLength = 255
            }
        });
    }
}
