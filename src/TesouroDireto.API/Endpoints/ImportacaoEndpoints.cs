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
        })
        .WithName("ImportarCsvTitulos")
        .WithTags("Importacao")
        .WithSummary("Importa títulos e preços/taxas a partir do CSV oficial")
        .WithDescription("Dispara a importação do CSV de títulos do Tesouro Direto (Tesouro Transparente), " +
            "criando/atualizando títulos e preços/taxas. Sem parâmetros; a URL de origem é de configuração.")
        .Produces<ImportResult>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/importacao/feriados", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ImportFeriadosCommand(), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("ImportarFeriados")
        .WithTags("Importacao")
        .WithSummary("Importa o calendário de feriados da ANBIMA")
        .WithDescription("Dispara a importação da planilha de feriados da ANBIMA, usada no cálculo de dias úteis. " +
            "Sem parâmetros; a URL de origem é de configuração.")
        .Produces<ImportFeriadosResult>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
