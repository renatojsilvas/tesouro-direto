using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public sealed record TipoTitulo
{
    public static readonly TipoTitulo TesouroPrefixado = new("Tesouro Prefixado", Indexador.Prefixado, false, 0.06m);
    public static readonly TipoTitulo TesouroPrefixadoComJuros = new("Tesouro Prefixado com Juros Semestrais", Indexador.Prefixado, true, 0.10m);
    public static readonly TipoTitulo TesouroSelic = new("Tesouro Selic", Indexador.Selic, false, 0.06m);
    public static readonly TipoTitulo TesouroIPCA = new("Tesouro IPCA+", Indexador.IPCA, false, 0.06m);
    public static readonly TipoTitulo TesouroIPCAComJuros = new("Tesouro IPCA+ com Juros Semestrais", Indexador.IPCA, true, 0.06m);
    public static readonly TipoTitulo TesouroIGPMComJuros = new("Tesouro IGPM+ com Juros Semestrais", Indexador.IGPM, true, 0.06m);
    public static readonly TipoTitulo TesouroEduca = new("Tesouro Educa+", Indexador.IPCA, false, 0.06m);
    public static readonly TipoTitulo TesouroRendaMais = new("Tesouro Renda+ Aposentadoria Extra", Indexador.IPCA, false, 0.06m);

    public static IReadOnlyCollection<TipoTitulo> All { get; } =
        [TesouroPrefixado, TesouroPrefixadoComJuros, TesouroSelic, TesouroIPCA, TesouroIPCAComJuros, TesouroIGPMComJuros, TesouroEduca, TesouroRendaMais];

    private const double FracaoDeAnoPorSemestre = 0.5;

    private TipoTitulo(string name, Indexador indexador, bool pagaJurosSemestrais, decimal taxaCupomAnual)
    {
        Name = name;
        Indexador = indexador;
        PagaJurosSemestrais = pagaJurosSemestrais;
        TaxaCupomAnual = taxaCupomAnual;
    }

    public string Name { get; }
    public Indexador Indexador { get; }
    public bool PagaJurosSemestrais { get; }
    public decimal TaxaCupomAnual { get; }
    public decimal TaxaCupomSemestral => (decimal)(Math.Pow(1.0 + (double)TaxaCupomAnual, FracaoDeAnoPorSemestre) - 1.0);

    public static Result<TipoTitulo> FromName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            return new Error("TipoTitulo.Invalid", "Tipo titulo name cannot be empty.");

        var match = All.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        var indexador = DeriveIndexador(trimmed);
        var pagaJuros = trimmed.Contains("Juros Semestrais", StringComparison.OrdinalIgnoreCase);

        return new TipoTitulo(trimmed, indexador, pagaJuros, 0.06m);
    }

    private static Indexador DeriveIndexador(string name)
    {
        if (name.Contains("Selic", StringComparison.OrdinalIgnoreCase))
            return Indexador.Selic;
        if (name.Contains("IPCA", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Educa", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Renda", StringComparison.OrdinalIgnoreCase))
            return Indexador.IPCA;
        if (name.Contains("IGPM", StringComparison.OrdinalIgnoreCase))
            return Indexador.IGPM;

        return Indexador.Prefixado;
    }
}
