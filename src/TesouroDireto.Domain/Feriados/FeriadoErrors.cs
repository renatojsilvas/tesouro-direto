using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Feriados;

public static class FeriadoErrors
{
    public static readonly Error InvalidData = new("DataFeriado.Invalid", "Data feriado must not be empty.");
    public static readonly Error InvalidDescricao = new("Feriado.InvalidDescricao", "Feriado descricao must not be empty.");
    public static readonly Error NotFound = new("Feriado.NotFound", "Feriado was not found.");
}
