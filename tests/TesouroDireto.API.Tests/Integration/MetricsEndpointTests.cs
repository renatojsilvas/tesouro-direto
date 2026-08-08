using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class MetricsEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _authenticatedClient = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metrics_AfterSuccessAndBusinessFailureRequests_ShouldExposeMediatrOutcomes()
    {
        var successResponse = await factory.CreateAuthenticatedClient().GetAsync("/v1/titulos", CancellationToken.None);
        successResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var failureResponse = await factory.CreateAuthenticatedClient()
            .GetAsync("/v1/titulos/preco-atual?nome=Tesouro Inexistente 2099", CancellationToken.None);
        failureResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var metricsResponse = await _client.GetAsync("/metrics", CancellationToken.None);
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

        body.Should().Contain("mediatr_request_duration_seconds");
        body.Should().Contain("mediatr_requests_total");
        body.Should().Contain("request_type=\"GetPrecoAtualByNomeQuery\",outcome=\"failure\"");
    }

    [Fact]
    public async Task Metrics_AfterSimulacaoFailure_ShouldIncrementSimulationFailureSeries()
    {
        var before = await ScrapeMetricsAsync();
        var simulationsBefore = ExtractCounterValue(before, "simulations_total", "indexador=\"unknown\"", "outcome=\"failure\"");
        var failuresBefore = ExtractCounterValue(before, "simulation_failures_total", "reason=\"Titulo.NotFound\"");

        var response = await _authenticatedClient.PostAsJsonAsync("/v1/simulador", new
        {
            Codigo = "tesouro-selic-2099-01-01",
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

    [Fact]
    public async Task Metrics_AfterImportacao_ShouldExposeImportSeries()
    {
        var beforeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var before = await ScrapeMetricsAsync();
        var errorsBefore = ExtractCounterValue(before, "import_prices_processed_total", "kind=\"error\"");

        var response = await _authenticatedClient.PostAsync("/v1/importacao", null, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var after = await ScrapeMetricsAsync();
        var errorsAfter = ExtractCounterValue(after, "import_prices_processed_total", "kind=\"error\"");
        var lastSuccessTimestamp = ExtractGaugeValue(after, "import_last_success_timestamp_seconds");

        lastSuccessTimestamp.Should().NotBeNull();
        lastSuccessTimestamp!.Value.Should().BeGreaterThan(0);
        lastSuccessTimestamp.Value.Should().BeInRange(beforeUnix - 2, afterUnix + 2);
        (errorsAfter - errorsBefore).Should().Be(1);
    }

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

        var response = await _authenticatedClient.PostAsJsonAsync("/v1/configuracoes/tributos", command, CancellationToken.None);
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

    private static double ExtractCounterValue(string body, string metricName, params string[] labelAssignments)
    {
        var lookaheads = string.Concat(labelAssignments.Select(kv => $"(?=[^\\n}}]*{Regex.Escape(kv)})"));
        var pattern = $@"(?m)^{Regex.Escape(metricName)}\{{{lookaheads}[^\n}}]*\}}\s+([0-9.eE+\-]+)$";
        var match = Regex.Match(body, pattern);
        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static double? ExtractGaugeValue(string body, string metricName)
    {
        var pattern = $@"(?m)^{Regex.Escape(metricName)} ([0-9.eE+\-]+)$";
        var match = Regex.Match(body, pattern);
        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }
}
