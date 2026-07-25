using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public sealed record Indexador
{
    public static readonly Indexador Selic = new("Selic");
    public static readonly Indexador Prefixado = new("Prefixado");
    public static readonly Indexador IPCA = new("IPCA");
    public static readonly Indexador IGPM = new("IGPM");

    public static IReadOnlyCollection<Indexador> All { get; } =
        [Selic, Prefixado, IPCA, IGPM];

    private Indexador(string name) => Name = name;

    public string Name { get; }

    public static Result<Indexador> FromName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var match = All.FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? match
            : new Error("Indexador.Invalid", $"'{name}' is not a valid indexador.");
    }

    /// <summary>
    /// Materializa um Indexador a partir de um valor persistido, sem falhar.
    /// Retorna o valor conhecido correspondente (case-insensitive) ou preserva
    /// o nome desconhecido — nunca rejeita, para não quebrar a materialização do EF.
    /// Use apenas na camada de persistência; para validar entrada use FromName.
    /// </summary>
    public static Indexador FromPersistence(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return All.FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? new Indexador(trimmed);
    }
}
