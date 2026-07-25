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

/// <summary>
/// Tarefa 13 (item b) — BCB circuit abre: prova, ponta a ponta, que depois de falhas
/// suficientes o circuit breaker abre e para de bater no BCB, caindo no fallback de cache
/// da tarefa 11 (não em 500).
///
/// Propositalmente NÃO usa a fixture compartilhada <see cref="ApiCollection"/> — o pipeline
/// de resiliência (e o estado do circuit breaker) é único por host, e este teste precisa de
/// um <c>MinimumThroughput</c> baixo o bastante para abrir o circuito de propósito, o que
/// vazaria (circuito aberto) para os outros ~10 arquivos de teste que usam o BCB através da
/// mesma fixture. Sobe seu próprio <see cref="ApiTestFactory"/>, com seu próprio Postgres
/// efêmero — mais lento, mas isolado (mesmo padrão já usado por outras suítes do projeto
/// que precisam de host/estado dedicado).
/// </summary>
public sealed class FocusBcbCircuitBreakerEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new()
    {
        ConfigOverrides = new Dictionary<string, string?>
        {
            // MaxAttempts=1 é o mínimo aceito pela biblioteca (Polly valida
            // MaxRetryAttempts >= 1, "0 tentativas" não existe como config). Cada POST
            // /simulador com falha ainda pode gerar até 2 execuções no circuit breaker
            // (tentativa inicial + 1 retry) até o circuito abrir — o teste não depende de
            // um número exato de chamadas por request, só compara a contagem total do
            // handler imediatamente antes/depois da última chamada.
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

    private async Task<Guid> SeedTituloAsync(TipoTitulo tipoTitulo, DateOnly dataVencimento)
    {
        var id = Guid.Empty;

        await _factory.SeedAsync(async sp =>
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
    public async Task PostSimulador_WhenBcbKeepsFailing_ShouldOpenCircuitAndFallBackToCache()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2033, 1, 1));

        var calls = 0;

        // 1ª chamada: sucesso — esquenta o cache "fresh" (TTL 2s) e "lkg" (last known good,
        // TTL default de produção, 7 dias).
        _factory.BcbResponder = _ =>
        {
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SelicSuccessJson, Encoding.UTF8, "application/json")
            };
        };

        var first = await _client.PostAsJsonAsync("/simulador", RequestBody(tituloId), CancellationToken.None);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(1);

        // FocusBcb:CacheTtl é 2s no host de teste — espera expirar o "fresh" (o "lkg"
        // continua válido, muito longe dos 7 dias de MaxFallbackAge).
        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);

        // BCB passa a falhar. Sem retry (MaxAttempts=0) e com MinimumThroughput=2 +
        // FailureRatio=0.5, o circuito deve abrir dentro das primeiras chamadas com falha.
        _factory.BcbResponder = _ =>
        {
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };

        for (var i = 0; i < 3; i++)
        {
            var response = await _client.PostAsJsonAsync("/simulador", RequestBody(tituloId), CancellationToken.None);

            // Mesmo com o BCB falhando, o Simulador nunca deveria devolver erro aqui: o
            // fallback de cache da tarefa 11 (independente do circuit breaker) já cobre
            // qualquer HttpError.
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var callsBeforeLastRequest = calls;

        var last = await _client.PostAsJsonAsync("/simulador", RequestBody(tituloId), CancellationToken.None);

        // Prova real do circuit breaker: a última chamada NÃO bateu no handler do BCB —
        // foi curto-circuitada antes de chegar lá.
        calls.Should().Be(callsBeforeLastRequest);

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await last.Content.ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        dto!.ProjecaoUtilizada.Should().NotBeNull();
        dto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.CacheFallback);
        dto.ProjecaoUtilizada!.ValorAnual.Should().Be(12.5m);
    }
}
