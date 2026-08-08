using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SimuladorCenariosMetricsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> SeedTituloAsync(TipoTitulo tipoTitulo, DateOnly dataVencimento)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(tipoTitulo, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
        });

        return TituloCodigo.From(tipoTitulo.Name, dataVencimento);
    }

    [Fact]
    public async Task PostSimuladorCenarios_WithTwoScenarios_ShouldIncrementSimulationsTotal()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2035, 5, 15));

        var before = await ScrapeSimulationsTotalAsync();

        var response = await _client.PostAsJsonAsync("/v1/simulador/cenarios", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            Cenarios = new[]
            {
                new { Nome = "Otimista", ProjecaoAnual = 3.5m },
                new { Nome = "Pessimista", ProjecaoAnual = 6.5m }
            }
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ScrapeSimulationsTotalAsync();

        after.Should().BeGreaterThan(before, "POST /simulador/cenarios deve incrementar simulations_total");
    }

    private async Task<double> ScrapeSimulationsTotalAsync()
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

            if (!line.StartsWith("simulations_total", StringComparison.Ordinal))
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
