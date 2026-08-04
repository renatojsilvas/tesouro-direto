using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tributos;

public static class TributoErrors
{
    public static readonly Error NotFound = new("Tributo.NotFound", "Tributo was not found.", ErrorType.NotFound);
    public static readonly Error InvalidNome = new("Tributo.InvalidNome", "Nome must not be empty.");
    public static readonly Error NoFaixas = new("Tributo.NoFaixas", "At least one faixa is required.");
    public static readonly Error InvalidOrdem = new("Tributo.InvalidOrdem", "Ordem must be zero or positive.");
    public static readonly Error AlreadyExists = new("Tributo.AlreadyExists", "Tributo already exists with the same nome.", ErrorType.Conflict);
}
