using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.ApiKeys;

public static class ApiKeyErrors
{
    public static readonly Error NotFound = new("ApiKey.NotFound", "Api key was not found.", ErrorType.NotFound);
    public static readonly Error AlreadyExists = new("ApiKey.AlreadyExists", "Api key already exists with same hash.", ErrorType.Conflict);
    public static readonly Error InvalidNome = new("ApiKey.InvalidNome", "Nome must not be empty.");
    public static readonly Error InvalidPrefixo = new("ApiKey.InvalidPrefixo", "Prefixo must not be empty.");
}
