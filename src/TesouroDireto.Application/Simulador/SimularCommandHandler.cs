using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Simulador;

public sealed class SimularCommandHandler(
    SimuladorApplicationService simulador,
    IProjecaoMercadoService projecaoService,
    IBusinessMetrics metrics) : IRequestHandler<SimularCommand, Result<SimulacaoResultadoDto>>
{
    public async Task<Result<SimulacaoResultadoDto>> Handle(SimularCommand request, CancellationToken cancellationToken)
    {
        var indexador = "unknown";
        var result = await ExecuteAsync(request, i => indexador = i, cancellationToken);
        metrics.RecordSimulation(indexador, result.IsSuccess ? "success" : "failure");
        if (result.IsFailure)
        {
            metrics.RecordSimulationFailure(result.Error.Code);
        }

        return result;
    }

    private async Task<Result<SimulacaoResultadoDto>> ExecuteAsync(
        SimularCommand request, Action<string> reportIndexador, CancellationToken cancellationToken)
    {
        var contextoResult = await simulador.CarregarContextoAsync(request.Codigo, request.DataCompra, cancellationToken);
        if (contextoResult.IsFailure)
        {
            return contextoResult.Error;
        }

        var contexto = contextoResult.Value;
        var titulo = contexto.Titulo;
        reportIndexador(titulo.Indexador.Name);

        var projecaoAnual = request.ProjecaoAnual;
        ProjecaoMercado? projecaoUtilizada = null;
        if (projecaoAnual is null && titulo.Indexador != Indexador.Prefixado)
        {
            var projecaoResult = await projecaoService.GetProjecaoAsync(titulo.Indexador, cancellationToken);
            if (projecaoResult.IsFailure)
            {
                return projecaoResult.Error;
            }

            projecaoUtilizada = projecaoResult.Value;
            projecaoAnual = projecaoResult.Value.MedianaAnual;
        }

        var simulacaoResult = simulador.Simular(contexto, request.ValorInvestido, request.TaxaContratada, projecaoAnual);
        if (simulacaoResult.IsFailure)
        {
            return simulacaoResult.Error;
        }

        return SimuladorApplicationService.MapToDto(simulacaoResult.Value, projecaoUtilizada);
    }
}
