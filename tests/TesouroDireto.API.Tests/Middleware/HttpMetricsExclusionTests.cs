using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class HttpMetricsExclusionTests : IClassFixture<HttpMetricsExclusionTests.MetricsWebFactory>
{
    private readonly HttpClient _client;

    public HttpMetricsExclusionTests(MetricsWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HttpMetrics_ShouldExcludeInfraPaths_ButKeepBusinessRoutes()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.GetAsync("/health", CancellationToken.None);
        }

        await _client.GetAsync("/health/live", CancellationToken.None);
        await _client.GetAsync("/health/ready", CancellationToken.None);
        await _client.GetAsync("/metrics", CancellationToken.None);

        await _client.GetAsync("/", CancellationToken.None);

        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        foreach (var series in new[] { "http_request_duration_seconds_count", "http_requests_received_total" })
        {
            foreach (var infraPath in new[] { "/health", "/health/live", "/health/ready", "/metrics" })
            {
                var pattern = new Regex(
                    $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""{Regex.Escape(infraPath)}""[^}}]*\}}");

                pattern.IsMatch(body).Should().BeFalse(
                    $"a série {series} não deveria conter o path de infra {infraPath} " +
                    $"(inflaria o denominador da regra de alerta td-http-5xx-alto).\nCorpo:\n{body}");
            }

            var rootPattern = new Regex(
                $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""/""[^}}]*\}}");

            rootPattern.IsMatch(body).Should().BeTrue(
                $"a rota de negócio \"/\" deveria continuar sendo contada pela série {series} (positivo de controle).\nCorpo:\n{body}");
        }
    }

    public sealed class MetricsWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = "test-key",
                    ["ApiKey:ExcludedPaths:0"] = "/health",
                    ["ApiKey:ExcludedPaths:1"] = "/metrics",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
