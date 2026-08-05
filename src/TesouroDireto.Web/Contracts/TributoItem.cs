namespace TesouroDireto.Web.Contracts;

public sealed record TributoItem(
    Guid Id,
    string Nome,
    string BaseCalculo,
    string TipoCalculo,
    List<FaixaItem> Faixas,
    bool Ativo,
    int Ordem,
    bool Cumulativo);

public sealed record FaixaItem(int? DiasMin, int? DiasMax, int? Dia, decimal Aliquota);
