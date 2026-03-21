using MediatR;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Simulador;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Simulador;

public sealed class SimularCommandHandler(
    ITituloWriteRepository tituloRepository,
    IDiasUteisService diasUteisService,
    IProjecaoMercadoService projecaoService,
    ITributoReadRepository tributoRepository,
    IFeriadoReadRepository feriadoRepository) : IRequestHandler<SimularCommand, Result<SimulacaoResultadoDto>>
{
    private static readonly SimuladorService Simulador = new();

    public async Task<Result<SimulacaoResultadoDto>> Handle(SimularCommand request, CancellationToken cancellationToken)
    {
        var tituloResult = await tituloRepository.GetByIdAsync(request.TituloId, cancellationToken);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        var titulo = tituloResult.Value;

        var duResult = await diasUteisService.CalcularDiasUteisAsync(
            request.DataCompra, titulo.DataVencimento.Value, cancellationToken);
        if (duResult.IsFailure)
        {
            return duResult.Error;
        }

        var diasCorridos = titulo.DataVencimento.Value.DayNumber - request.DataCompra.DayNumber;

        var projecaoAnual = request.ProjecaoAnual;
        if (projecaoAnual is null && titulo.Indexador != Indexador.Prefixado)
        {
            var projecaoResult = await projecaoService.GetProjecaoAsync(titulo.Indexador, cancellationToken);
            if (projecaoResult.IsFailure)
            {
                return projecaoResult.Error;
            }

            projecaoAnual = projecaoResult.Value.MedianaAnual;
        }

        var tributosResult = await tributoRepository.GetAtivosOrdenadosAsync(cancellationToken);
        if (tributosResult.IsFailure)
        {
            return tributosResult.Error;
        }

        var feriadosResult = await feriadoRepository.GetAllDatasAsync(cancellationToken);
        if (feriadosResult.IsFailure)
        {
            return feriadosResult.Error;
        }

        var input = new SimulacaoInput(
            titulo.TipoTitulo,
            request.ValorInvestido,
            request.TaxaContratada,
            request.DataCompra,
            titulo.DataVencimento.Value,
            duResult.Value,
            diasCorridos,
            projecaoAnual,
            feriadosResult.Value,
            tributosResult.Value);

        var simulacaoResult = Simulador.Simular(input);
        if (simulacaoResult.IsFailure)
        {
            return simulacaoResult.Error;
        }

        return MapToDto(simulacaoResult.Value);
    }

    private static SimulacaoResultadoDto MapToDto(SimulacaoResultado resultado)
    {
        var tributos = resultado.TributosAplicados
            .Select(t => new TributoAplicadoDto(t.Nome, t.Base, t.Aliquota, t.Valor))
            .ToList();

        var cupons = resultado.Cupons?
            .Select(c => new FluxoCupomDto(c.Data, c.ValorBruto, c.DiasUteis))
            .ToList();

        return new SimulacaoResultadoDto(
            resultado.ValorInvestido,
            resultado.ValorBruto,
            resultado.RendimentoBruto,
            tributos,
            resultado.TotalTributos,
            resultado.ValorLiquido,
            resultado.RendimentoLiquido,
            cupons);
    }
}
