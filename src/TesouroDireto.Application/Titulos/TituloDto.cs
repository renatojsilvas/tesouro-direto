namespace TesouroDireto.Application.Titulos;

public sealed record TituloDto(
    Guid Id,
    string TipoTitulo,
    string DataVencimento,
    string Indexador,
    bool PagaJurosSemestrais,
    bool Vencido);
