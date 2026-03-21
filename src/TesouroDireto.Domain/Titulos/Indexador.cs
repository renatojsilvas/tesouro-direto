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
}
