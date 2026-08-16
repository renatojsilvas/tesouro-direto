using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public static class PrecoTaxaErrors
{
    public static readonly Error NotFound = new("PrecoTaxa.NotFound", "PrecoTaxa was not found.", ErrorType.NotFound);
    public static readonly Error InvalidTituloId = new("PrecoTaxa.InvalidTituloId", "TituloId must not be empty.");
    public static readonly Error AlreadyExists = new("PrecoTaxa.AlreadyExists", "PrecoTaxa already exists for this titulo and date.", ErrorType.Conflict);
    public static readonly Error DataBaseInvalida = new("PrecoTaxa.DataBaseInvalida", "dataBase é obrigatória e deve estar no formato yyyy-MM-dd.");
    public static readonly Error DataBaseFutura = new("PrecoTaxa.DataBaseFutura", "dataBase não pode ser uma data futura.");
}
