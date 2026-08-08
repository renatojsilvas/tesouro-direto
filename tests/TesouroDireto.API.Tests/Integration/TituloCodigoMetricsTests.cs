using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class TituloCodigoMetricsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string PrecosEndpointTemplate =
        "/v1/titulos/{codigo:regex(^[a-z][a-z0-9-]*-\\\\d{{4}}-\\\\d{{2}}-\\\\d{{2}}$)}/precos";

    private const string PrecoAtualEndpointTemplate =
        "/v1/titulos/{codigo:regex(^[a-z][a-z0-9-]*-\\\\d{{4}}-\\\\d{{2}}-\\\\d{{2}}$)}/preco-atual";

    private const string CodigoSelic = "tesouro-selic-2029-03-01";
    private const string CodigoIpca = "tesouro-ipca-mais-2035-05-15";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metrics_AfterCallingByCodigoRoutesWithDifferentCodigos_ShouldExposeTemplateLabelNotLiteralSlug()
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var selic = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
            var ipca = Titulo.Create(TipoTitulo.TesouroIPCA, DataVencimento.Create(new DateOnly(2035, 5, 15)).Value).Value;
            await db.Titulos.AddRangeAsync(selic, ipca);
            await db.SaveChangesAsync();
        });

        var precosBefore = await ScrapeHttpMetricSumAsync($"endpoint=\"{PrecosEndpointTemplate}\"");
        var precoAtualBefore = await ScrapeHttpMetricSumAsync($"endpoint=\"{PrecoAtualEndpointTemplate}\"");

        await _client.GetAsync($"/v1/titulos/{CodigoSelic}/precos", CancellationToken.None);
        await _client.GetAsync($"/v1/titulos/{CodigoIpca}/precos", CancellationToken.None);
        await _client.GetAsync($"/v1/titulos/{CodigoSelic}/preco-atual", CancellationToken.None);
        await _client.GetAsync($"/v1/titulos/{CodigoIpca}/preco-atual", CancellationToken.None);

        var precosAfter = await ScrapeHttpMetricSumAsync($"endpoint=\"{PrecosEndpointTemplate}\"");
        var precoAtualAfter = await ScrapeHttpMetricSumAsync($"endpoint=\"{PrecoAtualEndpointTemplate}\"");

        precosAfter.Should().BeGreaterThan(precosBefore,
            "as duas chamadas com codigos diferentes deveriam incrementar a mesma série com label endpoint " +
            "igual ao TEMPLATE da rota, não uma série por codigo.");
        precoAtualAfter.Should().BeGreaterThan(precoAtualBefore,
            "as duas chamadas com codigos diferentes deveriam incrementar a mesma série com label endpoint " +
            "igual ao TEMPLATE da rota, não uma série por codigo.");

        var metricsResponse = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

        body.Should().NotContain(CodigoSelic,
            $"o slug literal {CodigoSelic} não deveria aparecer em nenhuma série de métricas HTTP (explodiria a cardinalidade).\n{body}");
        body.Should().NotContain(CodigoIpca,
            $"o slug literal {CodigoIpca} não deveria aparecer em nenhuma série de métricas HTTP (explodiria a cardinalidade).\n{body}");
    }

    private async Task<double> ScrapeHttpMetricSumAsync(string labelToken)
    {
        var response = await _client.GetAsync("/metrics", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        double sum = 0;
        foreach (var line in body.Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!line.StartsWith("http_request_duration_seconds_count", StringComparison.Ordinal)
                && !line.StartsWith("http_requests_received_total", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.Contains(labelToken, StringComparison.Ordinal))
            {
                continue;
            }

            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace >= 0 && double.TryParse(
                    line[(lastSpace + 1)..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                sum += value;
            }
        }

        return sum;
    }
}
