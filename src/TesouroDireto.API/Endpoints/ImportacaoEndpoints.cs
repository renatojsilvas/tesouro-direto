using MediatR;
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

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });

        app.MapPost("/importacao/feriados", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ImportFeriadosCommand(), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });
    }
}
