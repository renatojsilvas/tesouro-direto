using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Importacao;

public static class ImportacaoErrors
{
    public static Error InvalidLine(string detail) =>
        new("CsvImport.InvalidLine", detail);

    public static readonly Error InvalidTipoTitulo =
        new("CsvImport.InvalidTipoTitulo", "Tipo titulo is not recognized.");

    public static readonly Error InsufficientColumns =
        new("CsvImport.InsufficientColumns", "Line does not have 8 columns.");

    public static readonly Error EmptyLine =
        new("CsvImport.EmptyLine", "Line is empty.");
}
