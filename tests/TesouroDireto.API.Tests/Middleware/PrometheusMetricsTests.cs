using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class PrometheusMetricsTests : IClassFixture<PrometheusMetricsTests.MetricsWebFactory>
{
    private readonly HttpClient _client;

    public PrometheusMetricsTests(MetricsWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Metrics_ShouldReturn200_WithoutApiKey()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_ShouldReturnPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var content = await response.Content.ReadAsStringAsync(CancellationToken.None);

        content.Should().Contain("process_cpu_seconds_total",
            "Prometheus metrics should include default process metrics");
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
