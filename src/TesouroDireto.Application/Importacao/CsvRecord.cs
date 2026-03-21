namespace TesouroDireto.Application.Importacao;

public sealed record CsvRecord(
    string TipoTitulo,
    DateOnly DataVencimento,
    DateOnly DataBase,
    decimal TaxaCompra,
    decimal TaxaVenda,
    decimal PuCompra,
    decimal PuVenda,
    decimal PuBase);
