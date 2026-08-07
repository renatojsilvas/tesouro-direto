namespace TesouroDireto.Application.ApiKeys;

public sealed record GeneratedApiKeyDto(
    Guid Id,
    string Nome,
    string Prefixo,
    string Chave,
    DateTimeOffset CriadaEm);
