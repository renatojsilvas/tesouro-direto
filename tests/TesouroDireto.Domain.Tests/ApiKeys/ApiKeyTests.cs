using FluentAssertions;
using TesouroDireto.Domain.ApiKeys;

namespace TesouroDireto.Domain.Tests.ApiKeys;

public sealed class ApiKeyTests
{
    private static readonly ApiKeyHash ValidHash = ApiKeyHash.FromRawKey("chave-de-teste");
    private static readonly DateTimeOffset CriadaEm = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_ShouldReturnSuccessAndAtivaTrue()
    {
        var donoId = Guid.NewGuid();

        var result = ApiKey.Create("Sistema Parceiro", ValidHash, "abcd1234", donoId, CriadaEm);

        result.IsSuccess.Should().BeTrue();
        result.Value.Nome.Should().Be("Sistema Parceiro");
        result.Value.Hash.Should().Be(ValidHash);
        result.Value.Prefixo.Should().Be("abcd1234");
        result.Value.DonoUsuarioId.Should().Be(donoId);
        result.Value.CriadaEm.Should().Be(CriadaEm);
        result.Value.Ativa.Should().BeTrue();
        result.Value.RevogadaEm.Should().BeNull();
        result.Value.UltimoUsoEm.Should().BeNull();
        result.Value.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyNome_ShouldReturnFailure(string nome)
    {
        var result = ApiKey.Create(nome, ValidHash, "abcd1234", Guid.NewGuid(), CriadaEm);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.InvalidNome");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyPrefixo_ShouldReturnFailure(string prefixo)
    {
        var result = ApiKey.Create("Sistema", ValidHash, prefixo, Guid.NewGuid(), CriadaEm);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.InvalidPrefixo");
    }

    [Fact]
    public void Create_WithNullHash_ShouldThrow()
    {
        var act = () => ApiKey.Create("Sistema", null!, "abcd1234", Guid.NewGuid(), CriadaEm);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Revogar_ShouldSetAtivaFalseAndRevogadaEm()
    {
        var apiKey = ApiKey.Create("Sistema", ValidHash, "abcd1234", Guid.NewGuid(), CriadaEm).Value;
        var quando = CriadaEm.AddDays(10);

        apiKey.Revogar(quando);

        apiKey.Ativa.Should().BeFalse();
        apiKey.RevogadaEm.Should().Be(quando);
    }

    [Fact]
    public void RegistrarUso_ShouldSetUltimoUsoEm()
    {
        var apiKey = ApiKey.Create("Sistema", ValidHash, "abcd1234", Guid.NewGuid(), CriadaEm).Value;
        var quando = CriadaEm.AddHours(2);

        apiKey.RegistrarUso(quando);

        apiKey.UltimoUsoEm.Should().Be(quando);
    }
}
