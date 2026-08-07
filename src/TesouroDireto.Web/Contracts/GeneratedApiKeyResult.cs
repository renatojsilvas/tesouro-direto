namespace TesouroDireto.Web.Contracts;

public sealed record GeneratedApiKeyResult(
    Guid Id,
    string Nome,
    string Prefixo,
    string Chave,
    DateTimeOffset CriadaEm);
