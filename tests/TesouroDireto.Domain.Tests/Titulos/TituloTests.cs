using FluentAssertions;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Domain.Tests.Titulos;

public sealed class TituloTests
{
    [Fact]
    public void Create_WithValidData_ShouldReturnSuccess()
    {
        var tipo = TipoTitulo.TesouroSelic;
        var vencimento = DataVencimento.Create(new DateOnly(2029, 3, 1)).Value;

        var result = Titulo.Create(tipo, vencimento);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be(tipo);
        result.Value.DataVencimento.Should().Be(vencimento);
        result.Value.Indexador.Should().Be(Indexador.Selic);
        result.Value.PagaJurosSemestrais.Should().BeFalse();
    }

    [Fact]
    public void Create_TesouroIPCAComJuros_ShouldDeriveCorrectProperties()
    {
        var tipo = TipoTitulo.TesouroIPCAComJuros;
        var vencimento = DataVencimento.Create(new DateOnly(2035, 5, 15)).Value;

        var result = Titulo.Create(tipo, vencimento);

        result.IsSuccess.Should().BeTrue();
        result.Value.Indexador.Should().Be(Indexador.IPCA);
        result.Value.PagaJurosSemestrais.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldGenerateNonEmptyId()
    {
        var tipo = TipoTitulo.TesouroPrefixado;
        var vencimento = DataVencimento.Create(new DateOnly(2027, 1, 1)).Value;

        var titulo = Titulo.Create(tipo, vencimento).Value;

        titulo.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoTitulos_WithDifferentIds_ShouldNotBeEqual()
    {
        var tipo = TipoTitulo.TesouroSelic;
        var vencimento = DataVencimento.Create(new DateOnly(2029, 3, 1)).Value;

        var titulo1 = Titulo.Create(tipo, vencimento).Value;
        var titulo2 = Titulo.Create(tipo, vencimento).Value;

        titulo1.Should().NotBe(titulo2);
    }
}
