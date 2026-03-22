using MediatR;
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

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });

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

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });
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
