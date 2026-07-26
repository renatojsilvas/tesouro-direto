using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.API.Tests.Integration;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Resilience;

[Collection("api")]
public sealed class FocusBcbResilienceEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedTituloAsync(TipoTitulo tipoTitulo, DateOnly dataVencimento)
    {
        var id = Guid.Empty;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(tipoTitulo, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
            id = titulo.Id;
        });

        return id;
    }

    private static object RequestBody(Guid tituloId) => new
    {
        TituloId = tituloId,
        ValorInvestido = 1000m,
        DataCompra = new DateOnly(2024, 1, 2),
        TaxaContratada = 10m,
        ProjecaoAnual = (decimal?)null
    };

    [Fact]
    public async Task PostSimulador_WhenBcbFailsTwiceThenSucceeds_ShouldRetryAndReturn200()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2030, 1, 1));

        var calls = 0;
        factory.BcbResponder = _ =>
        {
            var attempt = Interlocked.Increment(ref calls);
            return attempt <= 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SelicSuccessJson, Encoding.UTF8, "application/json")
                };
        };

        var response = await _client.PostAsJsonAsync("/simulador", RequestBody(tituloId), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        dto!.ProjecaoUtilizada.Should().NotBeNull();
        dto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.Bcb);

        calls.Should().Be(3);
    }
}
