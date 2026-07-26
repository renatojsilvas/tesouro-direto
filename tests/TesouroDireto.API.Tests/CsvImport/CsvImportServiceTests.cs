using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Infrastructure.CsvImport;

namespace TesouroDireto.API.Tests.CsvImport;

public sealed class CsvImportServiceTests
{
    [Fact]
    public async Task GetRecordsAsync_WithBlankLine_ShouldReportPhysicalLineNumber()
    {
        var csv = string.Join('\n',
            "TipoTitulo;DataVencimento;DataBase;TaxaCompra;TaxaVenda;PuCompra;PuVenda;PuBase",
            "Tesouro Prefixado 2025;01/01/2025;02/01/2023;13,12;13,18;756,43;755,39;756,43",
            "",
            "Tesouro Prefixado 2025;invalid-date;02/01/2023;13,12;13,18;756,43;755,39;756,43");

        var service = CreateService(csv, "https://example.com/data.csv");

        var lines = new List<CsvRecordLine>();
        await foreach (var line in service.GetRecordsAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);

        lines[0].LineNumber.Should().Be(2);
        lines[0].Record.IsSuccess.Should().BeTrue();

        lines[1].LineNumber.Should().Be(4);
        lines[1].Record.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecordsAsync_WithMultipleBlankLines_ShouldKeepCountingPhysicalLines()
    {
        var csv = string.Join('\n',
            "TipoTitulo;DataVencimento;DataBase;TaxaCompra;TaxaVenda;PuCompra;PuVenda;PuBase",
            "",
            "",
            "Tesouro Prefixado 2025;01/01/2025;02/01/2023;13,12;13,18;756,43;755,39;756,43");

        var service = CreateService(csv, "https://example.com/data.csv");

        var lines = new List<CsvRecordLine>();
        await foreach (var line in service.GetRecordsAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().ContainSingle();
        lines[0].LineNumber.Should().Be(4);
        lines[0].Record.IsSuccess.Should().BeTrue();
    }

    private static CsvImportService CreateService(string csvContent, string url)
    {
        var handler = new FakeHttpMessageHandler(csvContent);
        var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CsvImport:Url"] = url
            })
            .Build();
        var logger = Substitute.For<ILogger<CsvImportService>>();

        return new CsvImportService(httpClient, configuration, logger);
    }

    private sealed class FakeHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content))
            };
            return Task.FromResult(response);
        }
    }
}
