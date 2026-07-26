using FluentAssertions;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Domain.Tests.Titulos;

public sealed class TituloCodigoTests
{
    [Theory]
    [InlineData("Tesouro Selic", 2029, 3, 1, "tesouro-selic-2029-03-01")]
    [InlineData("Tesouro IPCA+", 2029, 5, 15, "tesouro-ipca-mais-2029-05-15")]
    [InlineData("Tesouro Prefixado com Juros Semestrais", 2031, 1, 1, "tesouro-prefixado-com-juros-semestrais-2031-01-01")]
    [InlineData("Tesouro IPCA+ com Juros Semestrais", 2035, 5, 15, "tesouro-ipca-mais-com-juros-semestrais-2035-05-15")]
    [InlineData("Tesouro Educa+", 2030, 12, 15, "tesouro-educa-mais-2030-12-15")]
    [InlineData("Tesouro Renda+ Aposentadoria Extra", 2065, 12, 15, "tesouro-renda-mais-aposentadoria-extra-2065-12-15")]
    [InlineData("Tesouro Prefixado", 2027, 1, 1, "tesouro-prefixado-2027-01-01")]
    [InlineData("Tesouro IGPM+ com Juros Semestrais", 2032, 4, 15, "tesouro-igpm-mais-com-juros-semestrais-2032-04-15")]
    public void From_WithKnownTipoTitulo_ShouldReturnExpectedSlug(
        string tipoNome, int ano, int mes, int dia, string codigoEsperado)
    {
        var codigo = TituloCodigo.From(tipoNome, new DateOnly(ano, mes, dia));

        codigo.Should().Be(codigoEsperado);
    }

    [Fact]
    public void From_ForAllTipoTituloAll_ShouldProduceValidSlug()
    {
        foreach (var tipo in TipoTitulo.All)
        {
            var codigo = TituloCodigo.From(tipo.Name, new DateOnly(2030, 1, 1));

            codigo.Should().MatchRegex("^[a-z]+(-[a-z0-9]+)*-2030-01-01$");
        }
    }

    [Fact]
    public void TryParseDate_WithValidCodigo_ShouldReturnTrueAndDate()
    {
        var success = TituloCodigo.TryParseDate("tesouro-selic-2029-03-01", out var data);

        success.Should().BeTrue();
        data.Should().Be(new DateOnly(2029, 3, 1));
    }

    [Fact]
    public void TryParseDate_WithInvalidMonth_ShouldReturnFalse()
    {
        var success = TituloCodigo.TryParseDate("tesouro-selic-2029-13-40", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseDate_WithoutDateSuffix_ShouldReturnFalse()
    {
        var success = TituloCodigo.TryParseDate("tesouro-selic", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseDate_WithEmptyString_ShouldReturnFalse()
    {
        var success = TituloCodigo.TryParseDate("", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseDate_WithTruncatedGuidLikeString_ShouldReturnFalse()
    {
        var success = TituloCodigo.TryParseDate("12345678-1234-1234", out _);

        success.Should().BeFalse();
    }
}
