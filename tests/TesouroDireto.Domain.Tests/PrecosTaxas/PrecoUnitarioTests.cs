using FluentAssertions;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Domain.Tests.PrecosTaxas;

public sealed class PrecoUnitarioTests
{
    [Fact]
    public void Create_WithValidValue_ShouldReturnSuccess()
    {
        var result = PrecoUnitario.Create(1234.567890m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.567890m);
    }

    [Fact]
    public void Create_WithZero_ShouldReturnFailure()
    {
        var result = PrecoUnitario.Create(0m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PrecoUnitario.Invalid");
    }

    [Fact]
    public void Create_WithNegativeValue_ShouldReturnFailure()
    {
        var result = PrecoUnitario.Create(-100m);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SameValue_ShouldBeEqual()
    {
        var p1 = PrecoUnitario.Create(999.99m).Value;
        var p2 = PrecoUnitario.Create(999.99m).Value;

        p1.Should().Be(p2);
    }
}
