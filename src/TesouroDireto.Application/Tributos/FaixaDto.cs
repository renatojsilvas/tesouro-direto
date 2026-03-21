namespace TesouroDireto.Application.Tributos;

public sealed record FaixaDto(int? DiasMin, int? DiasMax, int? Dia, decimal Aliquota);
