using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public static class PrecoTaxaErrors
{
    public static readonly Error NotFound = new("PrecoTaxa.NotFound", "PrecoTaxa was not found.");
    public static readonly Error InvalidTituloId = new("PrecoTaxa.InvalidTituloId", "TituloId must not be empty.");
    public static readonly Error AlreadyExists = new("PrecoTaxa.AlreadyExists", "PrecoTaxa already exists for this titulo and date.");
}
