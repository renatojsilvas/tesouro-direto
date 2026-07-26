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

public sealed record ProjecaoUtilizadaDto(
    decimal ValorAnual,
    DateOnly DataReferencia,
    DateTimeOffset ObtidaEmUtc,
    OrigemProjecao Origem);
