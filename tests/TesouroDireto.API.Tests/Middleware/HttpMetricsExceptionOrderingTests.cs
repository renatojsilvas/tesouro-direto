using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class HttpMetricsExceptionOrderingTests : IClassFixture<HttpMetricsExceptionOrderingTests.OrderingWebFactory>
{
    private const string ThrowPath = "/_test/throw";
    private const string CountSeries = "http_requests_received_total";
    private const string DurationSeries = "http_request_duration_seconds_count";

    private readonly HttpClient _client;

    public HttpMetricsExceptionOrderingTests(OrderingWebFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add(OrderingWebFactory.ApiKeyHeader, OrderingWebFactory.ApiKeyValue);
    }

    [Fact]
    public async Task ThrowingEndpoint_ShouldRecordRealStatusCode_NotThePreExceptionOne()
    {
        var before = await ScrapeAsync();

        var response = await _client.GetAsync(ThrowPath, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "o UseExceptionHandler continua convertendo a exceção em 500, independente da ordem do middleware de métricas");

        var after = await ScrapeAsync();

        // Nesta factory, a ÚNICA rota jamais exercitada (fora do scrape de /metrics, que nem
        // é instrumentado — está em ApiKey:ExcludedPaths) é /_test/throw. Não é preciso filtrar
        // por "endpoint": o label code por si só já identifica a série gerada por esta chamada.
        // Achado factual (calibrado contra o corpo real de /metrics): o UseExceptionHandler
        // limpa o endpoint resolvido (context.SetEndpoint(null)) antes de reescrever a resposta,
        // então a série final gravada pelo UseHttpMetrics carrega endpoint="" para uma request
        // que terminou em exceção — só o http_requests_in_progress (gravado na ENTRADA, antes da
        // exceção) preserva endpoint="/_test/throw". O que a regra td-http-5xx-alto usa é o
        // `code`, não o `endpoint`, e é exatamente o que este teste prova.
        foreach (var series in new[] { CountSeries, DurationSeries })
        {
            var deltaCode500 = GetValue(after, series, "500") - GetValue(before, series, "500");
            var deltaCode200 = GetValue(after, series, "200") - GetValue(before, series, "200");

            deltaCode500.Should().Be(1,
                $"a requisição a {ThrowPath} terminou em 500 e a série {series} tem que refletir o label code=\"500\" " +
                "real, não o status default anterior ao UseExceptionHandler reescrever a resposta " +
                "(senão o alerta td-http-5xx-alto nunca vê o incidente).");

            deltaCode200.Should().Be(0,
                $"a série {series} não pode registrar code=\"200\" para {ThrowPath}: isso indicaria que o " +
                "UseHttpMetrics observou o status ANTES do UseExceptionHandler reescrever a resposta para 5xx.");
        }
    }

    private async Task<string> ScrapeAsync()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        return await response.Content.ReadAsStringAsync(CancellationToken.None);
    }

    private static double GetValue(string body, string series, string code)
    {
        var pattern = new Regex(
            $@"{Regex.Escape(series)}\{{code=""{Regex.Escape(code)}""[^}}]*\}}\s+(?<value>[0-9.eE+\-]+)");

        var match = pattern.Match(body);

        return match.Success
            ? double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture)
            : 0d;
    }

    public sealed class OrderingWebFactory : WebApplicationFactory<Program>
    {
        public const string ApiKeyHeader = "X-Api-Key";
        public const string ApiKeyValue = "ordering-test-key";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = ApiKeyValue,
                    ["ApiKey:ExcludedPaths:0"] = "/health",
                    ["ApiKey:ExcludedPaths:1"] = "/metrics",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
