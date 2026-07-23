using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using Xunit;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class TributosPadraoTests
{
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
        iof.Faixas.Single(f => f.Dia == 1).Aliquota.Should().Be(96m);
        iof.Faixas.Single(f => f.Dia == 29).Aliquota.Should().Be(3m);

        var ir = result.Value.Single(t => t.Nome == "Imposto de Renda");
        ir.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        ir.TipoCalculo.Should().Be(TipoCalculo.FaixaPorDias);
        ir.Cumulativo.Should().BeFalse();
        ir.Ordem.Should().Be(2);
        ir.Faixas.Should().HaveCount(4);
        ir.Faixas.Should().ContainSingle(f => f.DiasMin == 0 && f.DiasMax == 180 && f.Dia == null && f.Aliquota == 22.5m);
        ir.Faixas.Should().ContainSingle(f => f.DiasMin == 721 && f.DiasMax == 999999 && f.Aliquota == 15m);
    }
}
