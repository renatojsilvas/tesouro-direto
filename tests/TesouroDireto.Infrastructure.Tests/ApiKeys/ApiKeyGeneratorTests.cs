using FluentAssertions;
using TesouroDireto.Infrastructure.ApiKeys;

namespace TesouroDireto.Infrastructure.Tests.ApiKeys;

public sealed class ApiKeyGeneratorTests
{
    private readonly ApiKeyGenerator _generator = new();

    [Fact]
    public void Generate_ShouldReturnRawKeyStartingWithTdPrefix()
    {
        var material = _generator.Generate();

        material.RawKey.Should().StartWith("td_");
        material.Prefixo.Should().StartWith("td_");
    }

    [Fact]
    public void Generate_ShouldReturnRawKeyAsBase64UrlAfterPrefix()
    {
        var material = _generator.Generate();

        var encoded = material.RawKey["td_".Length..];
        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
    }

    [Fact]
    public void Generate_CalledTwice_ShouldReturnDifferentRawKeys()
    {
        var first = _generator.Generate();
        var second = _generator.Generate();

        first.RawKey.Should().NotBe(second.RawKey);
    }

    [Fact]
    public void Generate_ShouldReturnPrefixoAsPrefixOfRawKey()
    {
        var material = _generator.Generate();

        material.RawKey.Should().StartWith(material.Prefixo);
    }
}
