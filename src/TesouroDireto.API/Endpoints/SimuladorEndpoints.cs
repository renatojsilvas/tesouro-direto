using MediatR;
using TesouroDireto.API.Extensions;
using TesouroDireto.Application.Simulador;

namespace TesouroDireto.API.Endpoints;

public static class SimuladorEndpoints
{
    public static void MapSimuladorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/simulador", async (
            SimularRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new SimularCommand(
                request.TituloId,
                request.ValorInvestido,
                request.DataCompra,
                request.TaxaContratada,
                request.ProjecaoAnual);

            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("SimularInvestimento")
        .WithTags("Simulador")
        .WithSummary("Simula o rendimento de um investimento em um título")
        .WithDescription("Simula valor bruto/líquido, tributos aplicados e cupons (quando houver) para um título, " +
            "valor investido, data de compra e taxa contratada. ProjecaoAnual é opcional para títulos indexados " +
            "(sem ela, usa a projeção de mercado do BCB Focus). 404 se o título não existir ou não houver " +
            "projeção de mercado disponível para o indexador.")
        .Produces<SimulacaoResultadoDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/simulador/cenarios", async (
            SimularCenariosRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var cenarios = request.Cenarios
                .Select(c => new CenarioInput(c.Nome, c.ProjecaoAnual))
                .ToList();

            var command = new SimularCenariosCommand(
                request.TituloId,
                request.ValorInvestido,
                request.DataCompra,
                request.TaxaContratada,
                cenarios);

            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("SimularCenarios")
        .WithTags("Simulador")
        .WithSummary("Simula múltiplos cenários de projeção para o mesmo investimento")
        .WithDescription("Simula um mesmo título, valor investido, data de compra e taxa contratada sob vários " +
            "cenários nomeados, cada um com sua própria projeção anual informada explicitamente. " +
            "404 se o título não existir.")
        .Produces<IReadOnlyCollection<CenarioResultadoDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private sealed record SimularRequest(
        Guid TituloId,
        decimal ValorInvestido,
        DateOnly DataCompra,
        decimal TaxaContratada,
        decimal? ProjecaoAnual);

    private sealed record CenarioRequest(string Nome, decimal ProjecaoAnual);

    private sealed record SimularCenariosRequest(
        Guid TituloId,
        decimal ValorInvestido,
        DateOnly DataCompra,
        decimal TaxaContratada,
        IReadOnlyCollection<CenarioRequest> Cenarios);
}
