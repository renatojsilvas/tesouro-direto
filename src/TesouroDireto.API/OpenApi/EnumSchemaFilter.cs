using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TesouroDireto.API.OpenApi;

public sealed class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
        {
            return;
        }

        schema.Enum.Clear();
        schema.Type = "string";
        schema.Format = null;

        foreach (var name in Enum.GetNames(context.Type))
        {
            schema.Enum.Add(new OpenApiString(name));   
        }
    }
}
