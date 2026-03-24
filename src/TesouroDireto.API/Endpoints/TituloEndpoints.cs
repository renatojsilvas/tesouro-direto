using MediatR;
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

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });

        app.MapGet("/titulos/{id:guid}/precos", async (
            Guid id,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosQuery(id, dataInicio, dataFim), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(new { result.Error.Code, result.Error.Description });
        });

        app.MapGet("/titulos/{id:guid}/preco-atual", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualQuery(id), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(new { result.Error.Code, result.Error.Description });
        });

        app.MapGet("/titulos/preco-atual", async (
            string nome,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualByNomeQuery(nome), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(new { result.Error.Code, result.Error.Description });
        });

        app.MapGet("/titulos/precos", async (
            string nome,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosByNomeQuery(nome, dataInicio, dataFim), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(new { result.Error.Code, result.Error.Description });
        });
    }
}
