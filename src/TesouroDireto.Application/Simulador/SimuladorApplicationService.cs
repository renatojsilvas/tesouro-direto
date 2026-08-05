using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Simulador;

public sealed record SimulacaoContexto(
    Titulo Titulo,
    DateOnly DataCompra,
    int DiasUteis,
    int DiasCorridos,
    IReadOnlyCollection<DateOnly> Feriados,
    IReadOnlyCollection<Tributo> TributosAtivos);

public sealed class SimuladorApplicationService(
    ITituloReadRepository tituloReadRepository,
    ITituloWriteRepository tituloRepository,
    IDiasUteisService diasUteisService,
    ITributoReadRepository tributoRepository)
{
    private static readonly SimuladorService Simulador = new();

    public async Task<Result<SimulacaoContexto>> CarregarContextoAsync(
        string codigo, DateOnly dataCompra, CancellationToken cancellationToken)
    {
        var tituloIdResult = await tituloReadRepository.GetIdByCodigoAsync(codigo, cancellationToken);
        if (tituloIdResult.IsFailure)
        {
            return tituloIdResult.Error;
        }

        var tituloResult = await tituloRepository.GetByIdAsync(tituloIdResult.Value, cancellationToken);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        var titulo = tituloResult.Value;

        var duResult = await diasUteisService.CalcularDiasUteisAsync(
            dataCompra, titulo.DataVencimento.Value, cancellationToken);
        if (duResult.IsFailure)
        {
            return duResult.Error;
        }

        var diasCorridos = titulo.DataVencimento.Value.DayNumber - dataCompra.DayNumber;

        var tributosResult = await tributoRepository.GetAtivosOrdenadosAsync(cancellationToken);
        if (tributosResult.IsFailure)
        {
            return tributosResult.Error;
        }

        return Result<SimulacaoContexto>.Success(new SimulacaoContexto(
            titulo,
            dataCompra,
            duResult.Value.DiasUteis,
            diasCorridos,
            duResult.Value.Feriados,
            tributosResult.Value));
    }

    public Result<SimulacaoResultado> Simular(
        SimulacaoContexto contexto, decimal valorInvestido, decimal taxaContratada, decimal? projecaoAnual)
    {
        var input = new SimulacaoInput(
            contexto.Titulo.TipoTitulo,
            valorInvestido,
            taxaContratada,
            contexto.DataCompra,
            contexto.Titulo.DataVencimento.Value,
            contexto.DiasUteis,
            contexto.DiasCorridos,
            projecaoAnual,
            contexto.Feriados,
            contexto.TributosAtivos);

        return Simulador.Simular(input);
    }

    public static SimulacaoResultadoDto MapToDto(
        SimulacaoResultado resultado, ProjecaoMercado? projecaoUtilizada = null)
    {
        var tributos = resultado.TributosAplicados
            .Select(t => new TributoAplicadoDto(t.Nome, t.Base, t.Aliquota, t.Valor))
            .ToList();

        var cupons = resultado.Cupons?
            .Select(c => new FluxoCupomDto(c.Data, c.ValorBruto, c.DiasUteis))
            .ToList();

        var projecaoDto = projecaoUtilizada is null
            ? null
            : new ProjecaoUtilizadaDto(
                projecaoUtilizada.MedianaAnual,
                projecaoUtilizada.DataReferencia,
                projecaoUtilizada.ObtidaEmUtc,
                projecaoUtilizada.Origem);

        return new SimulacaoResultadoDto(
            resultado.ValorInvestido,
            resultado.ValorBruto,
            resultado.RendimentoBruto,
            tributos,
            resultado.TotalTributos,
            resultado.ValorLiquido,
            resultado.RendimentoLiquido,
            cupons,
            projecaoDto);
    }
}
