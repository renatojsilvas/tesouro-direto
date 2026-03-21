namespace TesouroDireto.Application.Importacao;

public sealed record ImportResult(
    int TitulosCriados,
    int PrecosInseridos,
    int PrecosIgnorados,
    int LinhasComErro);
