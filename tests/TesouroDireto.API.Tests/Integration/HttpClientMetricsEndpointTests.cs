using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class HttpClientMetricsEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private readonly HttpClient _metricsClient = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string SelicSuccessJson = """
        {
          "value": [{
            "Indicador": "Selic",
            "Data": "2026-01-01",
            "Media": 12.5,
            "Mediana": 12.5
          }]
        }
        """;

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
    public async Task Metrics_AfterSimulacaoCallsBcb_ShouldExposeFocusBcbServiceHttpClientDuration()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2030, 1, 1));

        factory.BcbResponder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SelicSuccessJson, Encoding.UTF8, "application/json")
        };

        var response = await _client.PostAsJsonAsync("/v1/simulador", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metricsResponse = await _metricsClient.GetAsync("/metrics", CancellationToken.None);
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

        body.Should().MatchRegex("httpclient_request_duration_seconds[^\\n{]*\\{[^\\n}]*client=\"FocusBcbService\"");
    }
}
