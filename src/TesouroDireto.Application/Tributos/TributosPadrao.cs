using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public static class TributosPadrao
{
    // Alíquotas de IOF por dia de resgate (índice 0 = dia 1), tabela regressiva de 29 dias.
    // Fonte: tests/TesouroDireto.E2E.Tests/seed.sql (memória project_tributos_configurados).
    private static readonly decimal[] IofAliquotas =
    [
        96m, 93m, 90m, 86m, 83m, 80m, 76m, 73m, 70m, 66m,
        63m, 60m, 56m, 53m, 50m, 46m, 43m, 40m, 36m, 33m,
        30m, 26m, 23m, 20m, 16m, 13m, 10m, 6m, 3m
    ];

    public static Result<IReadOnlyList<Tributo>> Build()
    {
        var iof = BuildIof();
        if (iof.IsFailure)
        {
            return iof.Error;
        }

        var ir = BuildIr();
        if (ir.IsFailure)
        {
            return ir.Error;
        }

        return Result<IReadOnlyList<Tributo>>.Success([iof.Value, ir.Value]);
    }

    private static Result<Tributo> BuildIof()
    {
        var faixas = new List<Faixa>(IofAliquotas.Length);
        for (var i = 0; i < IofAliquotas.Length; i++)
        {
            var faixa = Faixa.Create(diasMin: null, diasMax: null, dia: i + 1, aliquota: IofAliquotas[i]);
            if (faixa.IsFailure)
            {
                return faixa.Error;
            }

            faixas.Add(faixa.Value);
        }

        return Tributo.Create("IOF", BaseCalculo.Rendimento, TipoCalculo.TabelaDiaria, faixas, ordem: 1, cumulativo: true);
    }

    private static Result<Tributo> BuildIr()
    {
        (int DiasMin, int DiasMax, decimal Aliquota)[] specs =
        [
            (0, 180, 22.5m),
            (181, 360, 20m),
            (361, 720, 17.5m),
            (721, 999_999, 15m)
        ];

        var faixas = new List<Faixa>(specs.Length);
        foreach (var (diasMin, diasMax, aliquota) in specs)
        {
            var faixa = Faixa.Create(diasMin, diasMax, dia: null, aliquota: aliquota);
            if (faixa.IsFailure)
            {
                return faixa.Error;
            }

            faixas.Add(faixa.Value);
        }

        return Tributo.Create("Imposto de Renda", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, ordem: 2, cumulativo: false);
    }
}
