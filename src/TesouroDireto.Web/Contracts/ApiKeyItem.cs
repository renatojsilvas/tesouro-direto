namespace TesouroDireto.Web.Contracts;

public sealed record ApiKeyItem(
    Guid Id,
    string Nome,
    string Prefixo,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? UltimoUsoEm);
