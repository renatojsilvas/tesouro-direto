using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.Feriados;

namespace TesouroDireto.API.Tests.Resilience;

/// <summary>
/// Tarefa 13 (item c) — equivalente ao <see cref="CsvImportResilienceTests"/>, mas para o
/// download do XLS de feriados da ANBIMA. Reaproveita o XLS de teste embarcado usado por
/// <c>FeriadoImportServiceTests</c> (5 feriados esperados).
/// </summary>
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

        // Se o retry tivesse reprocessado o corpo, veríamos 10 registros em vez de 5.
        records.Should().HaveCount(5);

        // 1ª tentativa + 2 retries = 3 chamadas no handler HTTP.
        handler.Calls.Should().Be(3);
    }

    /// <summary>
    /// Correção pós-revisão (furo real, frente 4): o AttemptTimeout (tarefa 13) lança
    /// <c>Polly.Timeout.TimeoutRejectedException</c> quando um servidor lento estoura o
    /// tempo de tentativa mesmo depois dos retries — antes só <c>HttpRequestException</c>
    /// era capturada em <c>FeriadoImportService.GetFeriadosAsync</c>, então essa exceção
    /// escaparia do <c>await foreach</c> (500 genérico em <c>POST /importacao</c>, job
    /// Quartz sem log/métrica). Prova que agora degrada graciosamente: enumeração
    /// termina vazia (mesmo comportamento de qualquer outra falha de download hoje —
    /// URL ausente, HTTP não-2xx etc.), sem lançar nada para fora do enumerable.
    /// </summary>
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

        // Se TimeoutRejectedException não fosse capturada, esta linha lançaria e o teste
        // falharia com uma exceção não tratada (em vez de uma assertion comum) — é
        // exatamente o comportamento que o furo relatado pelo revisor descrevia.
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().BeEmpty();

        // 1ª tentativa + 1 retry = 2 chamadas, ambas estouraram o AttemptTimeout.
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
