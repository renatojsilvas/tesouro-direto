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
    public void FromName_WithKnownName_ShouldReturnKnownInstance(string name)
    {
        var result = TipoTitulo.FromName(name);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(name);
        TipoTitulo.All.Should().Contain(result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromName_WithEmptyName_ShouldReturnFailure(string name)
    {
        var result = TipoTitulo.FromName(name);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TipoTitulo.Invalid");
    }

    [Fact]
    public void FromName_WithNullName_ShouldReturnFailure()
    {
        var result = TipoTitulo.FromName(null!);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FromName_WithUnknownSelicName_ShouldDeriveIndexadorSelic()
    {
        var result = TipoTitulo.FromName("Tesouro Selic 2035");

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Tesouro Selic 2035");
        result.Value.Indexador.Should().Be(Indexador.Selic);
        result.Value.PagaJurosSemestrais.Should().BeFalse();
    }

    [Fact]
    public void FromName_WithUnknownIPCAName_ShouldDeriveIndexadorIPCA()
    {
        var result = TipoTitulo.FromName("Tesouro IPCA+ Especial");

        result.IsSuccess.Should().BeTrue();
        result.Value.Indexador.Should().Be(Indexador.IPCA);
    }

    [Fact]
    public void FromName_WithUnknownEducaName_ShouldDeriveIndexadorIPCA()
    {
        var result = TipoTitulo.FromName("Tesouro Educa+ 2040");

        result.IsSuccess.Should().BeTrue();
        result.Value.Indexador.Should().Be(Indexador.IPCA);
    }

    [Fact]
    public void FromName_WithUnknownIGPMName_ShouldDeriveIndexadorIGPM()
    {
        var result = TipoTitulo.FromName("Tesouro IGPM+ 2030");

        result.IsSuccess.Should().BeTrue();
        result.Value.Indexador.Should().Be(Indexador.IGPM);
    }

    [Fact]
    public void FromName_WithUnknownPrefixadoName_ShouldDefaultToPrefixado()
    {
        var result = TipoTitulo.FromName("Tesouro Novo Tipo");

        result.IsSuccess.Should().BeTrue();
        result.Value.Indexador.Should().Be(Indexador.Prefixado);
    }

    [Fact]
    public void FromName_WithUnknownJurosSemestrais_ShouldDetectJuros()
    {
        var result = TipoTitulo.FromName("Tesouro IPCA+ com Juros Semestrais Extra");

        result.IsSuccess.Should().BeTrue();
        result.Value.PagaJurosSemestrais.Should().BeTrue();
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
    public void All_ShouldReturnAllKnownTypes()
    {
        TipoTitulo.All.Should().HaveCount(8);
    }
}
