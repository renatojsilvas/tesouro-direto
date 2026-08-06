namespace TesouroDireto.Application.ApiKeys;

public sealed record ApiKeyDto(
    Guid Id,
    string Nome,
    string Prefixo,
    Guid DonoUsuarioId,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? UltimoUsoEm);
