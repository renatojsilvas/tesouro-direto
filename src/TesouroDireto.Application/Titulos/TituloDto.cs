namespace TesouroDireto.Application.Titulos;

public sealed record TituloDto(
    string TipoTitulo,
    string DataVencimento,
    string Indexador,
    bool PagaJurosSemestrais,
    bool Vencido,
    string Codigo);
