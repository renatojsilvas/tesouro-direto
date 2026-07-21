using MediatR;
using TesouroDireto.API.Extensions;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;

namespace TesouroDireto.API.Endpoints;

public static class ImportacaoEndpoints
{
    public static void MapImportacaoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/importacao", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ImportCsvCommand(), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        });

        app.MapPost("/importacao/feriados", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ImportFeriadosCommand(), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        });
    }
}
