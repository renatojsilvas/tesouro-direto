namespace TesouroDireto.Web.Contracts;

public sealed record SimulacaoResult(
    decimal ValorInvestido,
    decimal ValorBruto,
    decimal RendimentoBruto,
    List<TributoResult> TributosAplicados,
    decimal TotalTributos,
    decimal ValorLiquido,
    decimal RendimentoLiquido,
    List<CupomResult>? Cupons,
    ProjecaoUtilizadaResult? ProjecaoUtilizada);

public sealed record TributoResult(string Nome, decimal Base, decimal Aliquota, decimal Valor);

public sealed record CupomResult(DateOnly Data, decimal ValorBruto, int DiasUteis);

public sealed record ProjecaoUtilizadaResult(
    decimal ValorAnual,
    DateOnly DataReferencia,
    DateTimeOffset ObtidaEmUtc,
    string Origem);
