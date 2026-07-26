using FluentAssertions;
using TesouroDireto.Domain.DiasUteis;

namespace TesouroDireto.Domain.Tests.DiasUteis;

public sealed class DiasUteisCalculatorTests
{
    private readonly DiasUteisCalculator _calculator = new();

    [Fact]
    public void Calcular_SameDay_ShouldReturnZero()
    {
        var date = new DateOnly(2024, 7, 15);

        var result = _calculator.Calcular(date, date, []);

        result.Should().Be(0);
    }

    [Fact]
    public void Calcular_InicioAfterFim_ShouldReturnZero()
    {
        var inicio = new DateOnly(2024, 7, 16);
        var fim = new DateOnly(2024, 7, 15);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(0);
    }

    [Fact]
    public void Calcular_ConsecutiveWeekdays_ShouldCountCorrectly()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(4);
    }

    [Fact]
    public void Calcular_AcrossWeekend_ShouldExcludeWeekendDays()
    {
        var inicio = new DateOnly(2024, 7, 19);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_FullWeek_ShouldReturnFive()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(5);
    }

    [Fact]
    public void Calcular_TwoWeeks_ShouldReturnTen()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 29);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(10);
    }

    [Fact]
    public void Calcular_WithHolidayOnWeekday_ShouldExcludeHoliday()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);
        var feriados = new[] { new DateOnly(2024, 7, 17) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(3);
    }

    [Fact]
    public void Calcular_WithHolidayOnWeekend_ShouldNotDoubleCount()
    {
        var inicio = new DateOnly(2024, 7, 19);
        var fim = new DateOnly(2024, 7, 22);
        var feriados = new[] { new DateOnly(2024, 7, 20) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_WithMultipleHolidays_ShouldExcludeAll()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);
        var feriados = new[] { new DateOnly(2024, 7, 16), new DateOnly(2024, 7, 18) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(2);
    }

    [Fact]
    public void Calcular_LongerRange_ShouldBeAccurate()
    {
        var inicio = new DateOnly(2024, 1, 2);
        var fim = new DateOnly(2024, 1, 31);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(21);
    }

    [Fact]
    public void Calcular_InicioOnFriday_FimOnMonday_ShouldReturnOne()
    {
        var inicio = new DateOnly(2024, 7, 19);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_InicioOnSaturday_FimOnMonday_ShouldReturnOne()
    {
        var inicio = new DateOnly(2024, 7, 20);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_HolidayOnInicio_ShouldNotAffect()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 16);
        var feriados = new[] { new DateOnly(2024, 7, 15) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_HolidayOnFim_ShouldExclude()
    {
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 16);
        var feriados = new[] { new DateOnly(2024, 7, 16) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(0);
    }
}
