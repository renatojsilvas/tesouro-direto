using FluentAssertions;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Domain.Tests.Tributos;

public sealed class FaixaTests
{
    [Fact]
    public void Create_WithDiasMinAndMax_ShouldReturnSuccess()
    {
        var result = Faixa.Create(diasMin: 0, diasMax: 180, dia: null, aliquota: 22.5m);

        result.IsSuccess.Should().BeTrue();
        result.Value.DiasMin.Should().Be(0);
        result.Value.DiasMax.Should().Be(180);
        result.Value.Dia.Should().BeNull();
        result.Value.Aliquota.Should().Be(22.5m);
    }

    [Fact]
    public void Create_WithDiaOnly_ShouldReturnSuccess()
    {
        var result = Faixa.Create(diasMin: null, diasMax: null, dia: 1, aliquota: 96m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Dia.Should().Be(1);
    }

    [Fact]
    public void Create_WithNegativeAliquota_ShouldReturnFailure()
    {
        var result = Faixa.Create(diasMin: 0, diasMax: 180, dia: null, aliquota: -1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Faixa.InvalidAliquota");
    }

    [Fact]
    public void Create_WithAliquotaOver100_ShouldReturnFailure()
    {
        var result = Faixa.Create(diasMin: 0, diasMax: 180, dia: null, aliquota: 100.01m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Faixa.InvalidAliquota");
    }

    [Fact]
    public void Create_WithNoCriteria_ShouldReturnFailure()
    {
        var result = Faixa.Create(diasMin: null, diasMax: null, dia: null, aliquota: 15m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Faixa.NoCriteria");
    }

    [Fact]
    public void SameFaixa_ShouldBeEqual()
    {
        var f1 = Faixa.Create(0, 180, null, 22.5m).Value;
        var f2 = Faixa.Create(0, 180, null, 22.5m).Value;

        f1.Should().Be(f2);
    }
}
