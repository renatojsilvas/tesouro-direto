using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.CsvImport;

namespace TesouroDireto.API.Tests.Resilience;

public sealed class CsvImportResilienceTests
{
    private const string ValidCsv = """
        TipoTitulo;DataVencimento;DataBase;TaxaCompra;TaxaVenda;PuCompra;PuVenda;PuBase
        Tesouro Prefixado 2025;01/01/2025;02/01/2023;13,12;13,18;756,43;755,39;756,43
        Tesouro Prefixado 2027;01/01/2027;02/01/2023;13,50;13,60;700,00;699,00;700,00
        """;

    [Fact]
    public async Task GetRecordsAsync_WhenServerFailsTwiceThenSucceeds_ShouldRetryAndReturnRecordsWithoutDuplicates()
    {
        var handler = new SequenceHttpMessageHandler(
            failuresBeforeSuccess: 2,
            failureStatusCode: HttpStatusCode.ServiceUnavailable,
            successResponseFactory: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidCsv, Encoding.UTF8, "text/csv")
            });

        var service = BuildService(handler, "https://example.com/data.csv");

        var lines = new List<CsvRecordLine>();
        await foreach (var line in service.GetRecordsAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => l.Record.IsSuccess);

        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task GetRecordsAsync_WhenServerIsSlowerThanAttemptTimeout_ShouldDegradeGracefullyInsteadOfThrowing()
    {
        var handler = new DelayHttpMessageHandler(TimeSpan.FromMilliseconds(500));

        var service = BuildService(handler, "https://example.com/data.csv", new Dictionary<string, string?>
        {
            ["Resilience:CsvImport:Retry:MaxAttempts"] = "1",
            ["Resilience:CsvImport:AttemptTimeout"] = "00:00:00.050"
        });

        var lines = new List<CsvRecordLine>();

        await foreach (var line in service.GetRecordsAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().ContainSingle();
        lines[0].Record.IsFailure.Should().BeTrue();
        lines[0].Record.Error.Code.Should().Be("CsvImport.InvalidLine");

        handler.Calls.Should().Be(2);
    }

    private static ICsvImportService BuildService(
        HttpMessageHandler handler, string url, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["CsvImport:Url"] = url,
            ["Resilience:CsvImport:Retry:BaseDelay"] = "00:00:00.010"
        };

        if (extraConfig is not null)
        {
            foreach (var (key, value) in extraConfig)
            {
                config[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpClient<ICsvImportService, CsvImportService>()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddBatchImportResilienceHandler(configuration, "csv-import-resilience-test", "Resilience:CsvImport");

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ICsvImportService>();
    }
}
