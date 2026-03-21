using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public sealed record TipoTitulo
{
    public static readonly TipoTitulo TesouroPrefixado = new("Tesouro Prefixado", Indexador.Prefixado, false);
    public static readonly TipoTitulo TesouroPrefixadoComJuros = new("Tesouro Prefixado com Juros Semestrais", Indexador.Prefixado, true);
    public static readonly TipoTitulo TesouroSelic = new("Tesouro Selic", Indexador.Selic, false);
    public static readonly TipoTitulo TesouroIPCA = new("Tesouro IPCA+", Indexador.IPCA, false);
    public static readonly TipoTitulo TesouroIPCAComJuros = new("Tesouro IPCA+ com Juros Semestrais", Indexador.IPCA, true);
    public static readonly TipoTitulo TesouroIGPMComJuros = new("Tesouro IGPM+ com Juros Semestrais", Indexador.IGPM, true);
    public static readonly TipoTitulo TesouroEduca = new("Tesouro Educa+", Indexador.IPCA, false);
    public static readonly TipoTitulo TesouroRendaMais = new("Tesouro Renda+ Aposentadoria Extra", Indexador.IPCA, false);

    public static IReadOnlyCollection<TipoTitulo> All { get; } =
        [TesouroPrefixado, TesouroPrefixadoComJuros, TesouroSelic, TesouroIPCA, TesouroIPCAComJuros, TesouroIGPMComJuros, TesouroEduca, TesouroRendaMais];

    private TipoTitulo(string name, Indexador indexador, bool pagaJurosSemestrais)
    {
        Name = name;
        Indexador = indexador;
        PagaJurosSemestrais = pagaJurosSemestrais;
    }

    public string Name { get; }
    public Indexador Indexador { get; }
    public bool PagaJurosSemestrais { get; }

    public static Result<TipoTitulo> FromName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var match = All.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? match
            : new Error("TipoTitulo.Invalid", $"'{name}' is not a valid tipo titulo.");
    }
}
