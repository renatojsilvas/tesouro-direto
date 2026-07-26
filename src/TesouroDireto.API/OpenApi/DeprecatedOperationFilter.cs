using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TesouroDireto.API.OpenApi;

public sealed class DeprecatedOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var marker = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<DeprecatedApiMarker>()
            .FirstOrDefault();

        if (marker is null)
        {
            return;
        }

        operation.Deprecated = true;
        operation.Description = $"{marker.Message} {operation.Description}".Trim();
    }
}
