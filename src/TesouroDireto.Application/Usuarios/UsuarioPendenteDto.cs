namespace TesouroDireto.Application.Usuarios;

public sealed record UsuarioPendenteDto(
    Guid Id,
    string GoogleSub,
    string Email,
    string Nome,
    DateTimeOffset CriadoEm);
