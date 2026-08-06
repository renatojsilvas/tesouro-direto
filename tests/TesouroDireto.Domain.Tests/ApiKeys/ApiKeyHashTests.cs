using FluentAssertions;
using TesouroDireto.Domain.ApiKeys;

namespace TesouroDireto.Domain.Tests.ApiKeys;

public sealed class ApiKeyHashTests
{
    [Fact]
    public void FromRawKey_WithKnownAnswerTestVector_ShouldComputeExpectedSha256()
    {
        var hash = ApiKeyHash.FromRawKey("abc");

        hash.Value.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void FromRawKey_ShouldBeDeterministic()
    {
        var hash1 = ApiKeyHash.FromRawKey("minha-chave-secreta");
        var hash2 = ApiKeyHash.FromRawKey("minha-chave-secreta");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void FromRawKey_DifferentInputs_ShouldProduceDifferentHashes()
    {
        var hash1 = ApiKeyHash.FromRawKey("chave-um");
        var hash2 = ApiKeyHash.FromRawKey("chave-dois");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void FromRawKey_ShouldProduce64CharLowercaseHex()
    {
        var hash = ApiKeyHash.FromRawKey("qualquer-chave");

        hash.Value.Should().HaveLength(64);
        hash.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void FromRawKey_WithNull_ShouldThrow()
    {
        var act = () => ApiKeyHash.FromRawKey(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithValid64CharHex_ShouldReturnSuccess()
    {
        var validHash = new string('a', 64);

        var result = ApiKeyHash.Create(validHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(validHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Create_WithInvalidHash_ShouldReturnFailure(string invalidHash)
    {
        var result = ApiKeyHash.Create(invalidHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKeyHash.Invalid");
    }

    [Fact]
    public void Create_With65Chars_ShouldReturnFailure()
    {
        var result = ApiKeyHash.Create(new string('a', 65));

        result.IsFailure.Should().BeTrue();
    }
}
