using FluentAssertions;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Domain.Tests.Feriados;

public sealed class FeriadoTests
{
    [Fact]
    public void Create_WithValidData_ShouldReturnSuccess()
    {
        var data = DataFeriado.Create(new DateOnly(2024, 12, 25)).Value;

        var result = Feriado.Create(data, "Natal");

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Should().Be(data);
        result.Value.Descricao.Should().Be("Natal");
        result.Value.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithEmptyDescricao_ShouldReturnFailure()
    {
        var data = DataFeriado.Create(new DateOnly(2024, 12, 25)).Value;

        var result = Feriado.Create(data, "");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Feriado.InvalidDescricao");
    }

    [Fact]
    public void Create_WithWhitespaceDescricao_ShouldReturnFailure()
    {
        var data = DataFeriado.Create(new DateOnly(2024, 12, 25)).Value;

        var result = Feriado.Create(data, "   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Feriado.InvalidDescricao");
    }
}
