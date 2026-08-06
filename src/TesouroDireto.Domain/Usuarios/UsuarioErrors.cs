using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Usuarios;

public static class UsuarioErrors
{
    public static readonly Error NotFound = new("Usuario.NotFound", "Usuario was not found.", ErrorType.NotFound);
    public static readonly Error InvalidNome = new("Usuario.InvalidNome", "Nome must not be empty.");
    public static readonly Error AlreadyExists = new("Usuario.AlreadyExists", "Usuario already exists with same email.", ErrorType.Conflict);
    public static readonly Error GoogleSubJaVinculado = new("Usuario.GoogleSubJaVinculado", "Google sub is already linked to this usuario.");
}
