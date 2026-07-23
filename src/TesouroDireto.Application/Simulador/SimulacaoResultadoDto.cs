using TesouroDireto.Application.Projecoes;

namespace TesouroDireto.Application.Simulador;

public sealed record SimulacaoResultadoDto(
    decimal ValorInvestido,
    decimal ValorBruto,
    decimal RendimentoBruto,
    IReadOnlyCollection<TributoAplicadoDto> TributosAplicados,
    decimal TotalTributos,
    decimal ValorLiquido,
    decimal RendimentoLiquido,
    IReadOnlyCollection<FluxoCupomDto>? Cupons,
    ProjecaoUtilizadaDto? ProjecaoUtilizada = null);

public sealed record TributoAplicadoDto(string Nome, decimal Base, decimal Aliquota, decimal Valor);

public sealed record FluxoCupomDto(DateOnly Data, decimal ValorBruto, int DiasUteis);

/// <summary>
/// Sinaliza qual projeção de mercado foi de fato usada na simulação (quando o título é
/// indexado e o usuário não informou <c>ProjecaoAnual</c> explicitamente) e de onde ela
/// veio — <see cref="OrigemProjecao.Bcb"/> direto do Focus, ou
/// <see cref="OrigemProjecao.CacheFallback"/> quando o BCB estava indisponível e a
/// resposta usou a última projeção conhecida.
/// </summary>
public sealed record ProjecaoUtilizadaDto(
    decimal ValorAnual,
    DateOnly DataReferencia,
    DateTimeOffset ObtidaEmUtc,
    OrigemProjecao Origem);
