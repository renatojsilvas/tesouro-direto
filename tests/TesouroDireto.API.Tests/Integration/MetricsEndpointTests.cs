using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Tests.Integration;

/// <summary>
/// Prova, ponta a ponta (host real via Program.cs), que o MetricsBehavior (O6) está
/// plugado no pipeline do MediatR e que uma falha de negócio (Result.Failure, ex.:
/// Titulo.NotFound) vira outcome="failure" no Prometheus — nunca "exception". Também
/// prova (O8) que o Singleton IBusinessMetrics registrado em DependencyInjection
/// realmente emite as séries de negócio em /metrics — não apenas na classe
/// BusinessMetrics isolada ou em mocks de handler.
/// </summary>
[Collection("api")]
public sealed class MetricsEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    // /metrics é isento de ApiKey (ApiKey:ExcludedPaths) — cliente sem header.
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _authenticatedClient = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metrics_AfterSuccessAndBusinessFailureRequests_ShouldExposeMediatrOutcomes()
    {
        // Sucesso: GET /titulos passa pelo MediatR (GetTitulosQuery) e retorna 200.
        var successResponse = await factory.CreateAuthenticatedClient().GetAsync("/titulos", CancellationToken.None);
        successResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Falha de negócio (não exceção): nome de título inexistente -> Titulo.NotFound -> 404,
        // via GetPrecoAtualByNomeQuery/Result.Failure, sem exceção lançada.
        var failureResponse = await factory.CreateAuthenticatedClient()
            .GetAsync("/titulos/preco-atual?nome=Tesouro Inexistente 2099", CancellationToken.None);
        failureResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var metricsResponse = await _client.GetAsync("/metrics", CancellationToken.None);
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

        body.Should().Contain("mediatr_request_duration_seconds");
        body.Should().Contain("mediatr_requests_total");
        // Escopado por request_type: prova que ESTA query falha (GetPrecoAtualByNomeQuery)
        // virou outcome="failure" — não depende de poluição do registry global por outra
        // classe de teste (o registry do Prometheus é estático e compartilhado no processo).
        body.Should().Contain("request_type=\"GetPrecoAtualByNomeQuery\",outcome=\"failure\"");
    }

    // O8/E3-E4: prova que uma simulação que falha por título inexistente (determinística,
    // sem seed nem BCB) incrementa simulations_total{indexador="unknown",outcome="failure"}
    // e simulation_failures_total{reason="Titulo.NotFound"} no /metrics do host real.
    //
    // Como outras classes de teste do mesmo Collection("api") (ex.: SimuladorEndpointsTests)
    // também produzem a MESMA combinação de labels (indexador="unknown", reason=
    // "Titulo.NotFound"), o registry global do Prometheus pode já trazer essas séries
    // com valor > 0 antes deste teste rodar. Por isso a asserção é por DELTA
    // (antes/depois desta chamada específica), nunca por presença/valor absoluto —
    // prova não-vacuidade sem depender de ordem de execução entre classes.
    [Fact]
    public async Task Metrics_AfterSimulacaoFailure_ShouldIncrementSimulationFailureSeries()
    {
        var before = await ScrapeMetricsAsync();
        var simulationsBefore = ExtractCounterValue(before, "simulations_total", "indexador=\"unknown\"", "outcome=\"failure\"");
        var failuresBefore = ExtractCounterValue(before, "simulation_failures_total", "reason=\"Titulo.NotFound\"");

        var response = await _authenticatedClient.PostAsJsonAsync("/simulador", new
        {
            TituloId = Guid.NewGuid(),
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var after = await ScrapeMetricsAsync();
        var simulationsAfter = ExtractCounterValue(after, "simulations_total", "indexador=\"unknown\"", "outcome=\"failure\"");
        var failuresAfter = ExtractCounterValue(after, "simulation_failures_total", "reason=\"Titulo.NotFound\"");

        (simulationsAfter - simulationsBefore).Should().Be(1);
        (failuresAfter - failuresBefore).Should().Be(1);
    }

    // O8/E1-E2: prova que POST /importacao real (host real, handler real — o
    // ICsvImportService é stub em Testing por causa de CsvImport:Url=" ", mas o
    // ImportCsvCommandHandler roda de ponta a ponta e emite as séries de negócio)
    // atualiza import_last_success_timestamp_seconds e incrementa
    // import_prices_processed_total{kind="error"} (a URL não configurada gera 1 linha
    // com erro por chamada — ver ImportacaoEndpointsTests, comportamento determinístico
    // já documentado ali).
    [Fact]
    public async Task Metrics_AfterImportacao_ShouldExposeImportSeries()
    {
        var beforeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var before = await ScrapeMetricsAsync();
        var errorsBefore = ExtractCounterValue(before, "import_prices_processed_total", "kind=\"error\"");

        var response = await _authenticatedClient.PostAsync("/importacao", null, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var after = await ScrapeMetricsAsync();
        var errorsAfter = ExtractCounterValue(after, "import_prices_processed_total", "kind=\"error\"");
        var lastSuccessTimestamp = ExtractGaugeValue(after, "import_last_success_timestamp_seconds");

        lastSuccessTimestamp.Should().NotBeNull();
        lastSuccessTimestamp!.Value.Should().BeGreaterThan(0);
        // Prova que ESTA chamada (não uma anterior) marcou o gauge: o valor cai dentro
        // da janela de tempo desta requisição.
        lastSuccessTimestamp.Value.Should().BeInRange(beforeUnix - 2, afterUnix + 2);
        (errorsAfter - errorsBefore).Should().Be(1);
    }

    // O8: prova que POST /configuracoes/tributos (criação real, sem seed) incrementa
    // tributos_config_changes_total{op="create"} em /metrics.
    [Fact]
    public async Task Metrics_AfterCreateTributo_ShouldIncrementTributoConfigChangeSeries()
    {
        var before = await ScrapeMetricsAsync();
        var createsBefore = ExtractCounterValue(before, "tributos_config_changes_total", "op=\"create\"");

        var command = new CreateTributoCommand(
            "IOF Metrics Teste",
            BaseCalculo.Rendimento,
            TipoCalculo.TabelaDiaria,
            [new FaixaDto(0, 29, null, 96m), new FaixaDto(null, null, 29, 0m)],
            1,
            false);

        var response = await _authenticatedClient.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await ScrapeMetricsAsync();
        var createsAfter = ExtractCounterValue(after, "tributos_config_changes_total", "op=\"create\"");

        (createsAfter - createsBefore).Should().Be(1);
    }

    private async Task<string> ScrapeMetricsAsync()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(CancellationToken.None);
    }

    // Extrai o valor de uma série com labels, tolerando qualquer ORDEM de labels na
    // exposição (prometheus-net não garante ordem) — usa lookaheads por par
    // chave="valor" dentro das chaves da MESMA linha. Retorna 0 quando a série ainda
    // não existe (nenhuma observação até o momento do scrape).
    private static double ExtractCounterValue(string body, string metricName, params string[] labelAssignments)
    {
        var lookaheads = string.Concat(labelAssignments.Select(kv => $"(?=[^\\n}}]*{Regex.Escape(kv)})"));
        var pattern = $@"(?m)^{Regex.Escape(metricName)}\{{{lookaheads}[^\n}}]*\}}\s+([0-9.eE+\-]+)$";
        var match = Regex.Match(body, pattern);
        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    // Extrai o valor de uma métrica SEM labels (ex.: gauge de timestamp). Retorna null
    // quando a série ainda não existe.
    private static double? ExtractGaugeValue(string body, string metricName)
    {
        var pattern = $@"(?m)^{Regex.Escape(metricName)} ([0-9.eE+\-]+)$";
        var match = Regex.Match(body, pattern);
        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }
}
