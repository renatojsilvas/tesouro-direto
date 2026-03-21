using FluentAssertions;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Domain.Tests.Titulos;

public sealed class TipoTituloTests
{
    [Theory]
    [InlineData("Tesouro Prefixado")]
    [InlineData("Tesouro Prefixado com Juros Semestrais")]
    [InlineData("Tesouro Selic")]
    [InlineData("Tesouro IPCA+")]
    [InlineData("Tesouro IPCA+ com Juros Semestrais")]
    [InlineData("Tesouro IGPM+ com Juros Semestrais")]
    public void FromName_WithValidName_ShouldReturnSuccess(string name)
    {
        var result = TipoTitulo.FromName(name);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(name);
    }

    [Fact]
    public void FromName_WithInvalidName_ShouldReturnFailure()
    {
        var result = TipoTitulo.FromName("Titulo Inexistente");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TipoTitulo.Invalid");
    }

    [Fact]
    public void TesouroPrefixado_ShouldHaveCorrectIndexador()
    {
        TipoTitulo.TesouroPrefixado.Indexador.Should().Be(Indexador.Prefixado);
    }

    [Fact]
    public void TesouroSelic_ShouldHaveCorrectIndexador()
    {
        TipoTitulo.TesouroSelic.Indexador.Should().Be(Indexador.Selic);
    }

    [Fact]
    public void TesouroIPCA_ShouldHaveCorrectIndexador()
    {
        TipoTitulo.TesouroIPCA.Indexador.Should().Be(Indexador.IPCA);
    }

    [Fact]
    public void TesouroIGPMComJuros_ShouldHaveCorrectIndexador()
    {
        TipoTitulo.TesouroIGPMComJuros.Indexador.Should().Be(Indexador.IGPM);
    }

    [Fact]
    public void TiposComJurosSemestrais_ShouldReturnTrue()
    {
        TipoTitulo.TesouroPrefixadoComJuros.PagaJurosSemestrais.Should().BeTrue();
        TipoTitulo.TesouroIPCAComJuros.PagaJurosSemestrais.Should().BeTrue();
        TipoTitulo.TesouroIGPMComJuros.PagaJurosSemestrais.Should().BeTrue();
    }

    [Fact]
    public void TiposSemJurosSemestrais_ShouldReturnFalse()
    {
        TipoTitulo.TesouroPrefixado.PagaJurosSemestrais.Should().BeFalse();
        TipoTitulo.TesouroSelic.PagaJurosSemestrais.Should().BeFalse();
        TipoTitulo.TesouroIPCA.PagaJurosSemestrais.Should().BeFalse();
    }

    [Fact]
    public void SameTipoTitulo_ShouldBeEqual()
    {
        var tipo1 = TipoTitulo.FromName("Tesouro Selic").Value;
        var tipo2 = TipoTitulo.TesouroSelic;

        tipo1.Should().Be(tipo2);
    }

    [Fact]
    public void All_ShouldReturnAllTypes()
    {
        TipoTitulo.All.Should().HaveCount(8);
    }
}
