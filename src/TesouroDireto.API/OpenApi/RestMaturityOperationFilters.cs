using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TesouroDireto.API.OpenApi;

public sealed class PrecosPaginacaoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!MatchesEndpointName(context, "GetPrecosPorCodigo"))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "page",
            In = ParameterLocation.Query,
            Description = "Página (1-based). Sem esse parâmetro, retorna a coleção inteira como hoje.",
            Schema = new OpenApiSchema { Type = "integer" },
        });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "pageSize",
            In = ParameterLocation.Query,
            Description = "Tamanho da página (1 a 500, default 100, só considerado quando page é informado).",
            Schema = new OpenApiSchema { Type = "integer" },
        });

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

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "dataBase",
            In = ParameterLocation.Query,
            Required = true,
            Description = "Data do fechamento desejado (yyyy-MM-dd). Obrigatória.",
            Schema = new OpenApiSchema { Type = "string", Format = "date" },
        });

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
