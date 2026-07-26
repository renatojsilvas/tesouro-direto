using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TesouroDireto.API.OpenApi;

/// <summary>
/// Tarefa 33: a API serializa enums como string via <c>JsonStringEnumConverter</c> registrado
/// em <c>ConfigureHttpJsonOptions</c> (Http.Json.JsonOptions), mas o Swashbuckle 6.x monta o
/// schema a partir da reflection do CLR (não lê esse converter, que só é observado por
/// <c>System.Text.Json</c> em runtime) — sem este filtro, os enums (ex.: BaseCalculo,
/// TipoCalculo) apareceriam como <c>type: integer</c> no swagger.json, divergindo do payload
/// real trocado pela API.
/// </summary>
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
