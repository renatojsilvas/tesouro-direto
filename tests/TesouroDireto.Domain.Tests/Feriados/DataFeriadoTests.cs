using FluentAssertions;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Domain.Tests.Feriados;

public sealed class DataFeriadoTests
{
    [Fact]
    public void Create_WithValidDate_ShouldReturnSuccess()
    {
        var date = new DateOnly(2024, 12, 25);

        var result = DataFeriado.Create(date);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(date);
    }

    [Fact]
    public void Create_WithMinDate_ShouldReturnFailure()
    {
        var result = DataFeriado.Create(DateOnly.MinValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DataFeriado.Invalid");
    }

    [Fact]
    public void SameDate_ShouldBeEqual()
    {
        var date = new DateOnly(2024, 12, 25);
        var df1 = DataFeriado.Create(date).Value;
        var df2 = DataFeriado.Create(date).Value;

        df1.Should().Be(df2);
    }

    [Fact]
    public void DifferentDates_ShouldNotBeEqual()
    {
        var df1 = DataFeriado.Create(new DateOnly(2024, 12, 25)).Value;
        var df2 = DataFeriado.Create(new DateOnly(2024, 1, 1)).Value;

        df1.Should().NotBe(df2);
    }
}
