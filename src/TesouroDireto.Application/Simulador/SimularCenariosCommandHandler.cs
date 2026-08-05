using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Simulador;

public sealed class SimularCenariosCommandHandler(
    SimuladorApplicationService simulador,
    IBusinessMetrics metrics) : IRequestHandler<SimularCenariosCommand, Result<IReadOnlyCollection<CenarioResultadoDto>>>
{
    public async Task<Result<IReadOnlyCollection<CenarioResultadoDto>>> Handle(
        SimularCenariosCommand request, CancellationToken cancellationToken)
    {
        var indexador = "unknown";
        var contextoResult = await simulador.CarregarContextoAsync(request.Codigo, request.DataCompra, cancellationToken);
        if (contextoResult.IsFailure)
        {
            metrics.RecordSimulation(indexador, "failure");
            metrics.RecordSimulationFailure(contextoResult.Error.Code);
            return contextoResult.Error;
        }

        var contexto = contextoResult.Value;
        indexador = contexto.Titulo.Indexador.Name;

        var resultados = new List<CenarioResultadoDto>();
        foreach (var cenario in request.Cenarios)
        {
            var simulacaoResult = simulador.Simular(contexto, request.ValorInvestido, request.TaxaContratada, cenario.ProjecaoAnual);
            metrics.RecordSimulation(indexador, simulacaoResult.IsSuccess ? "success" : "failure");
            if (simulacaoResult.IsFailure)
            {
                metrics.RecordSimulationFailure(simulacaoResult.Error.Code);
                return simulacaoResult.Error;
            }

            resultados.Add(new CenarioResultadoDto(cenario.Nome, SimuladorApplicationService.MapToDto(simulacaoResult.Value)));
        }

        return Result<IReadOnlyCollection<CenarioResultadoDto>>.Success(resultados);
    }
}
