namespace TesouroDireto.Application.Projecoes;

/// <summary>
/// <paramref name="ObtidaEmUtc"/> e <paramref name="Origem"/> são obrigatórios de
/// propósito: não têm um valor "neutro" razoável (um <see cref="OrigemProjecao"/> não
/// informado apareceria silenciosamente como <see cref="OrigemProjecao.Bcb"/>, exatamente
/// o tipo de mascaramento que o fallback de cache desta camada existe para evitar).
/// </summary>
public sealed record ProjecaoMercado(
    string Indicador,
    DateOnly DataReferencia,
    decimal MediaAnual,
    decimal MedianaAnual,
    DateTimeOffset ObtidaEmUtc,
    OrigemProjecao Origem);
