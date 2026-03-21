using System.Globalization;
using System.Text.RegularExpressions;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.CsvImport;

public static partial class CsvParserHelper
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Result<CsvRecord> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return ImportacaoErrors.EmptyLine;
        }

        var columns = line.Split(';');
        if (columns.Length < 8)
        {
            return ImportacaoErrors.InsufficientColumns;
        }

        try
        {
            var tipoTitulo = StripTrailingYear(columns[0].Trim());
            var dataVencimento = DateOnly.ParseExact(columns[1].Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
            var dataBase = DateOnly.ParseExact(columns[2].Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
            var taxaCompra = decimal.Parse(columns[3].Trim(), PtBr);
            var taxaVenda = decimal.Parse(columns[4].Trim(), PtBr);
            var puCompra = decimal.Parse(columns[5].Trim(), PtBr);
            var puVenda = decimal.Parse(columns[6].Trim(), PtBr);
            var puBase = decimal.Parse(columns[7].Trim(), PtBr);

            return new CsvRecord(tipoTitulo, dataVencimento, dataBase, taxaCompra, taxaVenda, puCompra, puVenda, puBase);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return ImportacaoErrors.InvalidLine($"Failed to parse CSV columns: {ex.GetType().Name}");
        }
    }

    private static string StripTrailingYear(string tipoTitulo)
    {
        return TrailingYearRegex().Replace(tipoTitulo, string.Empty).Trim();
    }

    [GeneratedRegex(@"\s+\d{4}$")]
    private static partial Regex TrailingYearRegex();
}
