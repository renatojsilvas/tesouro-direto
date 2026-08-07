namespace TesouroDireto.Web.Contracts;

public sealed record UsuarioPendenteItem(
    Guid Id,
    string GoogleSub,
    string Email,
    string Nome,
    DateTimeOffset CriadoEm);
