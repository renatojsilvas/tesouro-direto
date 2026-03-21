using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tributos;

public sealed record Faixa
{
    private Faixa(int? diasMin, int? diasMax, int? dia, decimal aliquota)
    {
        DiasMin = diasMin;
        DiasMax = diasMax;
        Dia = dia;
        Aliquota = aliquota;
    }

    public int? DiasMin { get; }
    public int? DiasMax { get; }
    public int? Dia { get; }
    public decimal Aliquota { get; }

    public static Result<Faixa> Create(int? diasMin, int? diasMax, int? dia, decimal aliquota)
    {
        if (aliquota < 0 || aliquota > 100)
        {
            return new Error("Faixa.InvalidAliquota", "Aliquota must be between 0 and 100.");
        }

        if (diasMin is null && diasMax is null && dia is null)
        {
            return new Error("Faixa.NoCriteria", "At least one criteria (DiasMin, DiasMax, or Dia) must be provided.");
        }

        return new Faixa(diasMin, diasMax, dia, aliquota);
    }
}
