using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TesouroDireto.Application.Titulos;

namespace TesouroDireto.API.OpenApi;

public sealed class DeprecatedFieldsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(TituloDto))
        {
            return;
        }

        if (!schema.Properties.TryGetValue("id", out var idSchema))
        {
            return;
        }

        idSchema.Deprecated = true;
        idSchema.Description = "uuid interno em extinção - tarefa 38; prefira codigo";
    }
}
