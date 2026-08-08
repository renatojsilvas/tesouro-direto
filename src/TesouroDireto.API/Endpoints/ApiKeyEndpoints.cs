using MediatR;
using TesouroDireto.API.Contracts;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Http;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.API.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/keys", async (
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var usuario = (Usuario)httpContext.Items[UsuarioAprovadoFilter.UsuarioAprovadoItemsKey]!;
            var result = await sender.Send(new GetMinhasKeysQuery(usuario.Id), cancellationToken);

            return result.ToHttpResult(dtos => Results.Ok(dtos));
        })
        .AddEndpointFilter<UsuarioAprovadoFilter>()
        .WithName("GetMinhasApiKeys")
        .WithTags("ApiKeys")
        .WithSummary("Lista as API keys do usuário autenticado")
        .WithDescription("Retorna as API keys de propriedade do usuário autenticado, sem o texto puro da chave. " +
            "Exige usuário aprovado e ativo.")
        .Produces<IReadOnlyCollection<ApiKeyDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/me/keys", async (
            CreateApiKeyRequest request,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var usuario = (Usuario)httpContext.Items[UsuarioAprovadoFilter.UsuarioAprovadoItemsKey]!;
            var result = await sender.Send(new GenerateApiKeyCommand(usuario.Id, request.Nome), cancellationToken);

            return result.ToHttpResult(dto => Results.Created($"/v1/me/keys/{dto.Id}", dto));
        })
        .AddEndpointFilter<UsuarioAprovadoFilter>()
        .WithName("GenerateApiKey")
        .WithTags("ApiKeys")
        .WithSummary("Gera uma nova API key para o usuário autenticado")
        .WithDescription("Cria uma API key ativa vinculada ao usuário autenticado e retorna o texto puro da chave " +
            "uma única vez, no corpo da resposta. Exige usuário aprovado e ativo.")
        .Produces<GeneratedApiKeyDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/me/keys/{id:guid}/revogar", async (
            Guid id,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var usuario = (Usuario)httpContext.Items[UsuarioAprovadoFilter.UsuarioAprovadoItemsKey]!;
            var result = await sender.Send(new RevokeApiKeyCommand(id, usuario.Id), cancellationToken);

            return result.ToHttpResult(() => Results.NoContent());
        })
        .AddEndpointFilter<UsuarioAprovadoFilter>()
        .WithName("RevokeApiKey")
        .WithTags("ApiKeys")
        .WithSummary("Revoga uma API key do usuário autenticado")
        .WithDescription("Marca ativa=false na API key identificada por id, desde que pertença ao usuário autenticado. " +
            "204 em sucesso; 404 se a key não existir ou pertencer a outro usuário; 403 se o requisitante não estiver aprovado.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
