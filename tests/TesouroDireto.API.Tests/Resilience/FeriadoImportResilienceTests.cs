using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.Feriados;

namespace TesouroDireto.API.Tests.Resilience;

public sealed class FeriadoImportResilienceTests
{
    private const string TestXlsResource = "TesouroDireto.API.Tests.Feriados.feriados_test.xls";

    [Fact]
    public async Task GetFeriadosAsync_WhenServerFailsTwiceThenSucceeds_ShouldRetryAndReturnRecordsWithoutDuplicates()
    {
        var xlsBytes = GetTestXlsBytes();

        var handler = new SequenceHttpMessageHandler(
            failuresBeforeSuccess: 2,
            failureStatusCode: HttpStatusCode.ServiceUnavailable,
            successResponseFactory: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(xlsBytes)
            });

        var service = BuildService(handler, "https://example.com/feriados.xls");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().HaveCount(5);

        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task GetFeriadosAsync_WhenServerIsSlowerThanAttemptTimeout_ShouldDegradeGracefullyInsteadOfThrowing()
    {
        var handler = new DelayHttpMessageHandler(TimeSpan.FromMilliseconds(500));

        var service = BuildService(handler, "https://example.com/feriados.xls", new Dictionary<string, string?>
        {
            ["Resilience:FeriadoImport:Retry:MaxAttempts"] = "1",
            ["Resilience:FeriadoImport:AttemptTimeout"] = "00:00:00.050"
        });

        var records = new List<FeriadoRecord>();

        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().BeEmpty();

        handler.Calls.Should().Be(2);
    }

    private static byte[] GetTestXlsBytes()
    {
        using var stream = typeof(FeriadoImportResilienceTests).Assembly
            .GetManifestResourceStream(TestXlsResource)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static IFeriadoImportService BuildService(
        HttpMessageHandler handler, string url, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["FeriadoImport:Url"] = url,
            ["Resilience:FeriadoImport:Retry:BaseDelay"] = "00:00:00.010"
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
        services.AddHttpClient<IFeriadoImportService, FeriadoImportService>()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddBatchImportResilienceHandler(configuration, "feriado-import-resilience-test", "Resilience:FeriadoImport");

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IFeriadoImportService>();
    }
}
