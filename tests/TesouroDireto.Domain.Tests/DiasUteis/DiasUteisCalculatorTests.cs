using FluentAssertions;
using TesouroDireto.Domain.DiasUteis;

namespace TesouroDireto.Domain.Tests.DiasUteis;

public sealed class DiasUteisCalculatorTests
{
    private readonly DiasUteisCalculator _calculator = new();

    [Fact]
    public void Calcular_SameDay_ShouldReturnZero()
    {
        var date = new DateOnly(2024, 7, 15); // Monday

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
        // Mon Jul 15 to Fri Jul 19 = 4 business days (Tue, Wed, Thu, Fri)
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(4);
    }

    [Fact]
    public void Calcular_AcrossWeekend_ShouldExcludeWeekendDays()
    {
        // Fri Jul 19 to Mon Jul 22 = 1 business day (Mon only)
        var inicio = new DateOnly(2024, 7, 19);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_FullWeek_ShouldReturnFive()
    {
        // Mon Jul 15 to Mon Jul 22 = 5 business days
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 22);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(5);
    }

    [Fact]
    public void Calcular_TwoWeeks_ShouldReturnTen()
    {
        // Mon Jul 15 to Mon Jul 29 = 10 business days
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 29);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(10);
    }

    [Fact]
    public void Calcular_WithHolidayOnWeekday_ShouldExcludeHoliday()
    {
        // Mon Jul 15 to Fri Jul 19 = 4 days, minus 1 holiday (Wed Jul 17) = 3
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);
        var feriados = new[] { new DateOnly(2024, 7, 17) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(3);
    }

    [Fact]
    public void Calcular_WithHolidayOnWeekend_ShouldNotDoubleCount()
    {
        // Fri Jul 19 to Mon Jul 22 = 1 day, holiday on Sat Jul 20 should not affect
        var inicio = new DateOnly(2024, 7, 19);
        var fim = new DateOnly(2024, 7, 22);
        var feriados = new[] { new DateOnly(2024, 7, 20) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_WithMultipleHolidays_ShouldExcludeAll()
    {
        // Mon Jul 15 to Fri Jul 19 = 4 days, minus 2 holidays (Tue+Thu) = 2
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 19);
        var feriados = new[] { new DateOnly(2024, 7, 16), new DateOnly(2024, 7, 18) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(2);
    }

    [Fact]
    public void Calcular_LongerRange_ShouldBeAccurate()
    {
        // Jan 2, 2024 (Tue) to Jan 31, 2024 (Wed)
        // 21 weekdays from Jan 3 to Jan 31, minus 0 holidays = 21
        var inicio = new DateOnly(2024, 1, 2);
        var fim = new DateOnly(2024, 1, 31);

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(21);
    }

    [Fact]
    public void Calcular_InicioOnFriday_FimOnMonday_ShouldReturnOne()
    {
        var inicio = new DateOnly(2024, 7, 19); // Friday
        var fim = new DateOnly(2024, 7, 22);     // Monday

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_InicioOnSaturday_FimOnMonday_ShouldReturnOne()
    {
        var inicio = new DateOnly(2024, 7, 20); // Saturday
        var fim = new DateOnly(2024, 7, 22);     // Monday

        var result = _calculator.Calcular(inicio, fim, []);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_HolidayOnInicio_ShouldNotAffect()
    {
        // Inicio is not counted, so holiday on inicio date doesn't matter
        // Mon Jul 15 (holiday) to Tue Jul 16 = 1 day
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 16);
        var feriados = new[] { new DateOnly(2024, 7, 15) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(1);
    }

    [Fact]
    public void Calcular_HolidayOnFim_ShouldExclude()
    {
        // Mon Jul 15 to Tue Jul 16 (holiday) = 0 days
        var inicio = new DateOnly(2024, 7, 15);
        var fim = new DateOnly(2024, 7, 16);
        var feriados = new[] { new DateOnly(2024, 7, 16) };

        var result = _calculator.Calcular(inicio, fim, feriados);

        result.Should().Be(0);
    }
}
