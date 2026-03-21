namespace TesouroDireto.Domain.Simulador;

public sealed record SimulacaoResultado(
    decimal ValorInvestido,
    decimal ValorBruto,
    decimal RendimentoBruto,
    IReadOnlyCollection<TributoAplicado> TributosAplicados,
    decimal TotalTributos,
    decimal ValorLiquido,
    decimal RendimentoLiquido,
    IReadOnlyCollection<FluxoCupom>? Cupons);
