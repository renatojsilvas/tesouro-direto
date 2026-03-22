using MediatR;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Simulador;

namespace TesouroDireto.Application.Simulador;

public sealed class SimularCenariosCommandHandler(
    ITituloWriteRepository tituloRepository,
    IDiasUteisService diasUteisService,
    ITributoReadRepository tributoRepository,
    IFeriadoReadRepository feriadoRepository) : IRequestHandler<SimularCenariosCommand, Result<IReadOnlyCollection<CenarioResultadoDto>>>
{
    private static readonly SimuladorService Simulador = new();

    public async Task<Result<IReadOnlyCollection<CenarioResultadoDto>>> Handle(
        SimularCenariosCommand request, CancellationToken cancellationToken)
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

        var resultados = new List<CenarioResultadoDto>();

        foreach (var cenario in request.Cenarios)
        {
            var input = new SimulacaoInput(
                titulo.TipoTitulo,
                request.ValorInvestido,
                request.TaxaContratada,
                request.DataCompra,
                titulo.DataVencimento.Value,
                duResult.Value,
                diasCorridos,
                cenario.ProjecaoAnual,
                feriadosResult.Value,
                tributosResult.Value);

            var simulacaoResult = Simulador.Simular(input);
            if (simulacaoResult.IsFailure)
            {
                return simulacaoResult.Error;
            }

            resultados.Add(new CenarioResultadoDto(cenario.Nome, MapToDto(simulacaoResult.Value)));
        }

        return Result<IReadOnlyCollection<CenarioResultadoDto>>.Success(resultados);
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
