namespace TesouroDireto.Application.Projecoes;

public sealed record ProjecaoMercado(
    string Indicador,
    DateOnly DataReferencia,
    decimal MediaAnual,
    decimal MedianaAnual);
