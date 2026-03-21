using FluentAssertions;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Domain.Tests.Titulos;

public sealed class IndexadorTests
{
    [Theory]
    [InlineData("Selic")]
    [InlineData("Prefixado")]
    [InlineData("IPCA")]
    [InlineData("IGPM")]
    public void FromName_WithValidName_ShouldReturnSuccess(string name)
    {
        var result = Indexador.FromName(name);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(name);
    }

    [Fact]
    public void FromName_WithInvalidName_ShouldReturnFailure()
    {
        var result = Indexador.FromName("CDI");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Indexador.Invalid");
    }

    [Fact]
    public void SameIndexador_ShouldBeEqual()
    {
        var idx1 = Indexador.FromName("Selic").Value;
        var idx2 = Indexador.Selic;

        idx1.Should().Be(idx2);
    }

    [Fact]
    public void All_ShouldReturnAllIndexadores()
    {
        Indexador.All.Should().HaveCount(4);
    }
}
