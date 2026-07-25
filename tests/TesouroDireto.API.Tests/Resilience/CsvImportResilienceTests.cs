using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.CsvImport;

namespace TesouroDireto.API.Tests.Resilience;

/// <summary>
/// Tarefa 13 (item c) — prova que o pipeline de resiliência de fato re-tenta o download do
/// CSV do Tesouro Transparente quando o servidor falha algumas vezes antes de suceder, e
/// que isso NÃO gera duplicação de linhas processadas (o corpo só é lido depois de a
/// tentativa vencedora ter respondido headers + status 200 — as tentativas com falha nunca
/// chegam a produzir CsvRecordLine).
///
/// Monta o client via um ServiceCollection mínimo chamando exatamente o mesmo método de
/// extensão usado em produção (<see cref="DependencyInjection.AddBatchImportResilienceHandler"/>),
/// para não duplicar a configuração do pipeline e evitar drift entre teste e produção.
/// </summary>
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

        // 2 linhas de dados no CSV de teste, cada uma processada exatamente uma vez —
        // se o retry tivesse reprocessado o corpo (ou disparado 2 downloads bem-sucedidos),
        // veríamos 4 linhas em vez de 2.
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => l.Record.IsSuccess);

        // 1ª tentativa + 2 retries = 3 chamadas no handler HTTP.
        handler.Calls.Should().Be(3);
    }

    /// <summary>
    /// Correção pós-revisão (furo real, frente 4): o AttemptTimeout (tarefa 13) lança
    /// <c>Polly.Timeout.TimeoutRejectedException</c> quando um servidor lento estoura o
    /// tempo de tentativa mesmo depois dos retries — antes só <c>HttpRequestException</c>
    /// era capturada em <see cref="CsvImportService.SendRequestAsync"/>, então essa exceção
    /// escaparia do <c>await foreach</c> (500 genérico em <c>POST /importacao</c>, job
    /// Quartz sem log/métrica). Prova que agora degrada graciosamente: 1 registro de erro
    /// estruturado (<c>CsvImport.InvalidLine</c>), sem lançar nada para fora do
    /// enumerable.
    /// </summary>
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

        // Se TimeoutRejectedException não fosse capturada, esta linha lançaria e o teste
        // falharia com uma exceção não tratada (em vez de uma assertion comum) — é
        // exatamente o comportamento que o furo relatado pelo revisor descrevia.
        await foreach (var line in service.GetRecordsAsync(CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.Should().ContainSingle();
        lines[0].Record.IsFailure.Should().BeTrue();
        lines[0].Record.Error.Code.Should().Be("CsvImport.InvalidLine");

        // 1ª tentativa + 1 retry = 2 chamadas, ambas estouraram o AttemptTimeout.
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
