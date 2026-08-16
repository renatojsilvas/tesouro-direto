using System.Globalization;
using MediatR;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Http;
using TesouroDireto.Application.PrecosTaxas;

namespace TesouroDireto.API.Endpoints;

public static class PrecoEndpoints
{
    public static void MapPrecoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapReadGet("/precos", async (
            string? dataBase,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosPorDataQuery(dataBase), cancellationToken);

            return result.ToHttpResult(v =>
            {
                httpContext.Response.Headers["X-Total-Count"] = v.Count.ToString(CultureInfo.InvariantCulture);

                return Results.Ok(v);
            });
        })
        .WithName("GetPrecosPorData")
        .WithTags("Precos")
        .WithSummary("Lista o fechamento de todos os títulos numa data")
        .WithDescription("Retorna o preço/taxa de todos os títulos na data informada (dataBase, query " +
            "obrigatória, formato yyyy-MM-dd), em 1 requisição. Dia sem pregão (fim de semana, feriado ou " +
            "importação ainda não executada) devolve 200 com lista vazia, não 404. 400 se dataBase estiver " +
            "ausente, mal formada ou for uma data futura.")
        .Produces<IReadOnlyList<PrecoTaxaDiaDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
