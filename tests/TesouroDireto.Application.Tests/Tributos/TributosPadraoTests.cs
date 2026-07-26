using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using Xunit;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class TributosPadraoTests
{
    private static readonly decimal[] IofAliquotasEsperadas =
    [
        96m, 93m, 90m, 86m, 83m, 80m, 76m, 73m, 70m, 66m,
        63m, 60m, 56m, 53m, 50m, 46m, 43m, 40m, 36m, 33m,
        30m, 26m, 23m, 20m, 16m, 13m, 10m, 6m, 3m
    ];

    private static readonly (int? DiasMin, int? DiasMax, decimal Aliquota)[] IrFaixasEsperadas =
    [
        (0, 180, 22.5m),
        (181, 360, 20m),
        (361, 720, 17.5m),
        (721, 999_999, 15m)
    ];

    [Fact]
    public void Build_ShouldReturnIofAndIr_WithCanonicalValues()
    {
        var result = TributosPadrao.Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        var iof = result.Value.Single(t => t.Nome == "IOF");
        iof.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        iof.TipoCalculo.Should().Be(TipoCalculo.TabelaDiaria);
        iof.Cumulativo.Should().BeTrue();
        iof.Ordem.Should().Be(1);
        iof.Faixas.Should().HaveCount(29);
        iof.Faixas.Should().OnlyContain(f => f.DiasMin == null && f.DiasMax == null && f.Dia != null);

        var iofOrdenadas = iof.Faixas.OrderBy(f => f.Dia).ToList();
        iofOrdenadas.Select(f => f.Dia).Should().Equal(Enumerable.Range(1, 29).Cast<int?>());
        iofOrdenadas.Select(f => f.Aliquota).Should().Equal(IofAliquotasEsperadas);

        var ir = result.Value.Single(t => t.Nome == "Imposto de Renda");
        ir.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        ir.TipoCalculo.Should().Be(TipoCalculo.FaixaPorDias);
        ir.Cumulativo.Should().BeFalse();
        ir.Ordem.Should().Be(2);
        ir.Faixas.Should().HaveCount(4);
        ir.Faixas.Should().OnlyContain(f => f.Dia == null);

        var irOrdenadas = ir.Faixas.OrderBy(f => f.DiasMin).ToList();
        irOrdenadas.Select(f => (f.DiasMin, f.DiasMax, f.Aliquota)).Should().Equal(IrFaixasEsperadas);
    }
}
