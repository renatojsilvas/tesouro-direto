using FluentAssertions;
using TesouroDireto.Infrastructure.CsvImport;

namespace TesouroDireto.API.Tests.CsvImport;

public sealed class CsvParserHelperTests
{
    [Fact]
    public void ParseLine_ValidLine_ShouldReturnSuccess()
    {
        var line = "Tesouro Prefixado 2025;01/01/2025;02/01/2023;13,12;13,18;756,432100;755,390300;756,432100";

        var result = CsvParserHelper.ParseLine(line);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be("Tesouro Prefixado");
        result.Value.DataVencimento.Should().Be(new DateOnly(2025, 1, 1));
        result.Value.DataBase.Should().Be(new DateOnly(2023, 1, 2));
        result.Value.TaxaCompra.Should().Be(13.12m);
        result.Value.TaxaVenda.Should().Be(13.18m);
        result.Value.PuCompra.Should().Be(756.432100m);
        result.Value.PuVenda.Should().Be(755.390300m);
        result.Value.PuBase.Should().Be(756.432100m);
    }

    [Fact]
    public void ParseLine_ShouldStripYearFromTipoTitulo()
    {
        var line = "Tesouro IPCA+ com Juros Semestrais 2040;01/05/2040;15/03/2024;6,50;6,55;1200,123456;1199,654321;1198,111111";

        var result = CsvParserHelper.ParseLine(line);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be("Tesouro IPCA+ com Juros Semestrais");
    }

    [Fact]
    public void ParseLine_InsufficientColumns_ShouldReturnFailure()
    {
        var line = "Tesouro Prefixado 2025;01/01/2025;02/01/2023";

        var result = CsvParserHelper.ParseLine(line);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CsvImport.InsufficientColumns");
    }

    [Fact]
    public void ParseLine_InvalidDate_ShouldReturnFailure()
    {
        var line = "Tesouro Prefixado 2025;invalid-date;02/01/2023;13,12;13,18;756,43;755,39;756,43";

        var result = CsvParserHelper.ParseLine(line);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CsvImport.InvalidLine");
    }

    [Fact]
    public void ParseLine_EmptyLine_ShouldReturnFailure()
    {
        var result = CsvParserHelper.ParseLine("");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CsvImport.EmptyLine");
    }

    [Fact]
    public void ParseLine_InvalidDecimal_ShouldReturnFailure()
    {
        var line = "Tesouro Prefixado 2025;01/01/2025;02/01/2023;abc;13,18;756,43;755,39;756,43";

        var result = CsvParserHelper.ParseLine(line);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CsvImport.InvalidLine");
    }
}
