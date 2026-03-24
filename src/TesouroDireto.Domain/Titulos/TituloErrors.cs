using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public static class TituloErrors
{
    public static readonly Error NotFound = new("Titulo.NotFound", "Titulo was not found.");
    public static readonly Error InvalidNome = new("Titulo.InvalidNome", "Nome must not be empty.");
    public static readonly Error AlreadyExists = new("Titulo.AlreadyExists", "Titulo already exists with same type and maturity date.");
}
