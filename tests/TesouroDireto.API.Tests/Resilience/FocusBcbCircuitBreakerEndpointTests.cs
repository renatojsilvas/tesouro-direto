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

public sealed class FocusBcbCircuitBreakerEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new()
    {
        ConfigOverrides = new Dictionary<string, string?>
        {
            ["Resilience:FocusBcb:Retry:MaxAttempts"] = "1",
            ["Resilience:FocusBcb:Retry:BaseDelay"] = "00:00:00.010",
            ["Resilience:FocusBcb:CircuitBreaker:MinimumThroughput"] = "2",
            ["Resilience:FocusBcb:CircuitBreaker:SamplingDuration"] = "00:00:10",
            ["Resilience:FocusBcb:CircuitBreaker:BreakDuration"] = "00:00:05",
            ["Resilience:FocusBcb:CircuitBreaker:FailureRatio"] = "0.5"
        }
    };

    private HttpClient _client = null!;

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

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<string> SeedTituloAsync(TipoTitulo tipoTitulo, DateOnly dataVencimento)
    {
        await _factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(tipoTitulo, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
        });

        return TituloCodigo.From(tipoTitulo.Name, dataVencimento);
    }

    private static object RequestBody(string codigo) => new
    {
        Codigo = codigo,
        ValorInvestido = 1000m,
        DataCompra = new DateOnly(2024, 1, 2),
        TaxaContratada = 10m,
        ProjecaoAnual = (decimal?)null
    };

    [Fact]
    public async Task PostSimulador_WhenBcbKeepsFailing_ShouldOpenCircuitAndFallBackToCache()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2033, 1, 1));

        var calls = 0;

        _factory.BcbResponder = _ =>
        {
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SelicSuccessJson, Encoding.UTF8, "application/json")
            };
        };

        var first = await _client.PostAsJsonAsync("/v1/simulador", RequestBody(codigo), CancellationToken.None);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(1);

        _factory.AdvanceTime(TimeSpan.FromSeconds(3));

        _factory.BcbResponder = _ =>
        {
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };

        for (var i = 0; i < 3; i++)
        {
            var response = await _client.PostAsJsonAsync("/v1/simulador", RequestBody(codigo), CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var callsBeforeLastRequest = calls;

        var last = await _client.PostAsJsonAsync("/v1/simulador", RequestBody(codigo), CancellationToken.None);

        calls.Should().Be(callsBeforeLastRequest);

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await last.Content.ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        dto!.ProjecaoUtilizada.Should().NotBeNull();
        dto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.CacheFallback);
        dto.ProjecaoUtilizada!.ValorAnual.Should().Be(12.5m);
    }
}
