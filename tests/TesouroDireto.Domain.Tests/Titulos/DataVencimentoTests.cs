using FluentAssertions;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Domain.Tests.Titulos;

public sealed class DataVencimentoTests
{
    [Fact]
    public void Create_WithValidDate_ShouldReturnSuccess()
    {
        var date = new DateOnly(2030, 1, 1);

        var result = DataVencimento.Create(date);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(date);
    }

    [Fact]
    public void Create_WithMinDate_ShouldReturnFailure()
    {
        var result = DataVencimento.Create(DateOnly.MinValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DataVencimento.Invalid");
    }

    [Fact]
    public void SameDate_ShouldBeEqual()
    {
        var date = new DateOnly(2030, 1, 1);
        var dv1 = DataVencimento.Create(date).Value;
        var dv2 = DataVencimento.Create(date).Value;

        dv1.Should().Be(dv2);
    }
}
