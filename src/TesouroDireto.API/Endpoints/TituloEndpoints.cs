using MediatR;
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
    }
}
