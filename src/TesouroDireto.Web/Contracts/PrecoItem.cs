namespace TesouroDireto.Web.Contracts;

public sealed record PrecoItem(
    string DataBase,
    decimal? TaxaCompra,
    decimal? TaxaVenda,
    decimal? PuCompra,
    decimal? PuVenda,
    decimal? PuBase);
