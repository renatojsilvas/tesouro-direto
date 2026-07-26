using MediatR;
using TesouroDireto.API.Extensions;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;

namespace TesouroDireto.API.Endpoints;

public static class TituloEndpoints
{
    public static void MapTituloEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/titulos", async (
            string? indexador,
            bool? vencido,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTitulosQuery(indexador, vencido), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetTitulos")
        .WithTags("Titulos")
        .WithSummary("Lista os títulos do Tesouro Direto")
        .WithDescription("Retorna os títulos cadastrados, com filtro opcional por indexador (nome do indexador) " +
            "e por vencido (true/false). Sem filtros, retorna todos os títulos.")
        .Produces<IReadOnlyCollection<TituloDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet("/titulos/{id:guid}/precos", async (
            Guid id,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosQuery(id, dataInicio, dataFim), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecosPorTituloId")
        .WithTags("Titulos")
        .WithSummary("Lista o histórico de preços/taxas de um título")
        .WithDescription("Retorna o histórico de preços e taxas do título identificado por id, com filtro opcional " +
            "por intervalo de datas (dataInicio, dataFim). 404 se o título não existir.")
        .Produces<IReadOnlyCollection<PrecoTaxaDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/titulos/{id:guid}/preco-atual", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualQuery(id), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecoAtualPorTituloId")
        .WithTags("Titulos")
        .WithSummary("Retorna o preço/taxa mais recente de um título")
        .WithDescription("Retorna o preço e taxa mais recentes do título identificado por id. " +
            "404 se o título não existir.")
        .Produces<PrecoTaxaDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/titulos/preco-atual", async (
            string nome,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualByNomeQuery(nome), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecoAtualPorNome")
        .WithTags("Titulos")
        .WithSummary("Retorna o preço/taxa mais recente de um título pelo nome")
        .WithDescription("Retorna o preço e taxa mais recentes do título cujo nome (query obrigatória) é informado. " +
            "400 se nome for vazio; 404 se nenhum título com esse nome existir.")
        .Produces<PrecoTaxaDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/titulos/precos", async (
            string nome,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosByNomeQuery(nome, dataInicio, dataFim), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecosPorNome")
        .WithTags("Titulos")
        .WithSummary("Lista o histórico de preços/taxas de um título pelo nome")
        .WithDescription("Retorna o histórico de preços e taxas do título cujo nome (query obrigatória) é informado, " +
            "com filtro opcional por intervalo de datas (dataInicio, dataFim). " +
            "400 se nome for vazio; 404 se nenhum título com esse nome existir.")
        .Produces<IReadOnlyCollection<PrecoTaxaDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
