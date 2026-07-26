using MediatR;
using TesouroDireto.API.Contracts;
using TesouroDireto.API.Extensions;
using TesouroDireto.Application.Tributos;

namespace TesouroDireto.API.Endpoints;

public static class ConfiguracaoEndpoints
{
    public static void MapConfiguracaoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/configuracoes/tributos", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTributosQuery(), cancellationToken);

            return result.ToHttpResult(dtos => Results.Ok(dtos));
        })
        .WithName("GetTributos")
        .WithTags("Tributos")
        .WithSummary("Lista as configurações de tributos")
        .WithDescription("Retorna todos os tributos configurados (IOF, IR etc.), ativos ou não, com suas faixas.")
        .Produces<IReadOnlyCollection<TributoDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPut("/configuracoes/tributos/{id:guid}", async (
            Guid id,
            UpdateTributoRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTributoCommand(id, request.Ativo, request.Faixas);
            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(() => Results.NoContent());
        })
        .WithName("UpdateTributo")
        .WithTags("Tributos")
        .WithSummary("Atualiza um tributo existente")
        .WithDescription("Atualiza o estado ativo/inativo e as faixas do tributo identificado por id. " +
            "204 em sucesso; 400 se as faixas forem inválidas; 404 se o tributo não existir.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/configuracoes/tributos", async (
            CreateTributoRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTributoCommand(
                request.Nome,
                request.BaseCalculo,
                request.TipoCalculo,
                request.Faixas,
                request.Ordem,
                request.Cumulativo);
            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(id => Results.Created($"/configuracoes/tributos/{id}", new { Id = id }));
        })
        .WithName("CreateTributo")
        .WithTags("Tributos")
        .WithSummary("Cria um novo tributo")
        .WithDescription("Cria um tributo com nome, base de cálculo, tipo de cálculo, faixas, ordem e cumulatividade. " +
            "201 com o id criado em sucesso; 400 se os dados forem inválidos.")
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    public sealed record UpdateTributoRequest(bool Ativo, IReadOnlyCollection<FaixaDto> Faixas);
}
