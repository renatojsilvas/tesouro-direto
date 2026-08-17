using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TesouroDireto.API.OpenApi;

internal static class OperationFilterExtensions
{
    /// <summary>
    /// O ApiExplorer já descobre sozinho parâmetros de query ligados a argumentos do handler
    /// (ex.: page, pageSize, dataBase), então esses filtros existem só para enriquecer a
    /// descrição/schema/obrigatoriedade — nunca para adicionar uma segunda entrada duplicada.
    /// O Add só acontece como fallback defensivo, caso o ApiExplorer algum dia deixe de descobrir.
    /// </summary>
    public static void UpsertQueryParameter(
        this OpenApiOperation operation,
        string name,
        string description,
        string type,
        string? format = null,
        bool? required = null)
    {
        var existing = operation.Parameters.FirstOrDefault(
            p => p.In == ParameterLocation.Query && p.Name == name);

        if (existing is null)
        {
            existing = new OpenApiParameter { Name = name, In = ParameterLocation.Query };
            operation.Parameters.Add(existing);
        }

        existing.Description = description;

        // Enriquecer, não substituir: o schema do ApiExplorer já traz format (int32 em page/pageSize),
        // que se perderia num Schema novo. Só sobrescreve o format quem informa um.
        existing.Schema ??= new OpenApiSchema();
        existing.Schema.Type = type;
        if (format is not null)
        {
            existing.Schema.Format = format;
        }

        if (required.HasValue)
        {
            existing.Required = required.Value;
        }
    }
}

public sealed class PrecosPaginacaoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!MatchesEndpointName(context, "GetPrecosPorCodigo"))
        {
            return;
        }

        operation.UpsertQueryParameter(
            "page",
            "Página (1-based). Sem esse parâmetro, retorna a coleção inteira como hoje.",
            "integer");
        operation.UpsertQueryParameter(
            "pageSize",
            "Tamanho da página (1 a 500, default 100, só considerado quando page é informado).",
            "integer");

        if (!operation.Responses.TryGetValue("200", out var okResponse))
        {
            return;
        }

        okResponse.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Versão do conteúdo para requisições condicionais (If-None-Match).",
            Schema = new OpenApiSchema { Type = "string" },
        };
        okResponse.Headers["X-Total-Count"] = new OpenApiHeader
        {
            Description = "Total de itens (considerando os filtros de data, ignorando a paginação).",
            Schema = new OpenApiSchema { Type = "integer" },
        };
        okResponse.Headers["Link"] = new OpenApiHeader
        {
            Description = "Navegação RFC 8288 (rels first/prev/next/last), presente apenas quando paginado.",
            Schema = new OpenApiSchema { Type = "string" },
        };
    }

    internal static bool MatchesEndpointName(OperationFilterContext context, string endpointName) =>
        context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<EndpointNameMetadata>()
            .Any(metadata => metadata.EndpointName == endpointName);
}

public sealed class PrecosPorDataOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!PrecosPaginacaoOperationFilter.MatchesEndpointName(context, "GetPrecosPorData"))
        {
            return;
        }

        operation.UpsertQueryParameter(
            "dataBase",
            "Data do fechamento desejado (yyyy-MM-dd). Obrigatória.",
            "string",
            format: "date",
            required: true);

        if (!operation.Responses.TryGetValue("200", out var okResponse))
        {
            return;
        }

        okResponse.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Versão do conteúdo para requisições condicionais (If-None-Match).",
            Schema = new OpenApiSchema { Type = "string" },
        };
        okResponse.Headers["X-Total-Count"] = new OpenApiHeader
        {
            Description = "Total de itens (títulos com preço nesta data).",
            Schema = new OpenApiSchema { Type = "integer" },
        };
    }
}

public sealed class TributoLocationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!PrecosPaginacaoOperationFilter.MatchesEndpointName(context, "CreateTributo"))
        {
            return;
        }

        if (!operation.Responses.TryGetValue("201", out var createdResponse))
        {
            return;
        }

        createdResponse.Headers["Location"] = new OpenApiHeader
        {
            Description = "URI do tributo criado; GET nesse URI retorna 200 com o tributo.",
            Schema = new OpenApiSchema { Type = "string" },
        };
    }
}
