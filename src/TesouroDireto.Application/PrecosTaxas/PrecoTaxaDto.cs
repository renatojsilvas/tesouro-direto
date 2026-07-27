namespace TesouroDireto.Application.PrecosTaxas;

public sealed record PrecoTaxaDto(
    string DataBase,
    decimal? TaxaCompra,
    decimal? TaxaVenda,
    decimal? PuCompra,
    decimal? PuVenda,
    decimal? PuBase);
