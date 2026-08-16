namespace TesouroDireto.Application.PrecosTaxas;

public sealed record PrecoTaxaDiaDto(
    string Codigo, string DataBase,
    decimal? TaxaCompra, decimal? TaxaVenda,
    decimal? PuCompra, decimal? PuVenda, decimal? PuBase);
