using FluentAssertions;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Domain.Tests.PrecosTaxas;

public sealed class TaxaTests
{
    [Fact]
    public void Create_WithValidValue_ShouldReturnSuccess()
    {
        var result = Taxa.Create(10.25m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(10.25m);
    }

    [Fact]
    public void Create_WithZero_ShouldReturnSuccess()
    {
        var result = Taxa.Create(0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(0m);
    }

    [Fact]
    public void Create_WithNegativeValue_ShouldReturnFailure()
    {
        var result = Taxa.Create(-1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Taxa.Invalid");
    }

    [Fact]
    public void SameValue_ShouldBeEqual()
    {
        var t1 = Taxa.Create(5.50m).Value;
        var t2 = Taxa.Create(5.50m).Value;

        t1.Should().Be(t2);
    }
}
