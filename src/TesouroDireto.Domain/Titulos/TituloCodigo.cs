using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TesouroDireto.Domain.Titulos;

public static partial class TituloCodigo
{
    public static string From(string tipoNome, DateOnly data)
    {
        var comMais = tipoNome.Replace("+", " mais ");
        var semAcentos = RemoveDiacritics(comMais);
        var minusculo = semAcentos.ToLowerInvariant();
        var slug = NaoAlfanumericoRegex().Replace(minusculo, "-").Trim('-');

        return $"{slug}-{data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseDate(string codigo, out DateOnly data)
    {
        data = default;

        var match = CodigoRegex().Match(codigo);
        if (!match.Success)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            match.Groups[1].Value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out data);
    }

    private static string RemoveDiacritics(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizado.Length);

        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NaoAlfanumericoRegex();

    [GeneratedRegex(@"^[a-z]+(?:-[a-z0-9]+)*-(\d{4}-\d{2}-\d{2})$")]
    private static partial Regex CodigoRegex();
}
