using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SimuladorEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    // O JsonSerializerOptions default de HttpContent.ReadFromJsonAsync não conhece o
    // JsonStringEnumConverter registrado só no lado do servidor (ConfigureHttpJsonOptions
    // em Program.cs) — precisa ser espelhado aqui para desserializar ProjecaoUtilizadaDto.Origem.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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

    // 14. POST /simulador título Prefixado, inputs válidos -> 200 SimulacaoResultadoDto
    [Fact]
    public async Task PostSimulador_WithPrefixadoTitulo_ShouldReturn200()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SimulacaoResultadoDto>(cancellationToken: CancellationToken.None);
        dto.Should().NotBeNull();
        dto!.ValorInvestido.Should().Be(1000m);
        dto.ValorBruto.Should().BeGreaterThan(0);
    }

    // 15. POST /simulador título indexado + ProjecaoAnual explícito -> 200 (sem BCB)
    [Fact]
    public async Task PostSimulador_WithIndexedTituloAndExplicitProjecao_ShouldReturn200WithoutBcbCall()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2035, 5, 15));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            ProjecaoAnual = 4.5m
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SimulacaoResultadoDto>(cancellationToken: CancellationToken.None);
        dto.Should().NotBeNull();
        dto!.ValorInvestido.Should().Be(1000m);
    }

    // 16. POST /simulador TituloId inexistente -> 404 (Titulo.NotFound via ResultExtensions.ToHttpResult)
    [Fact]
    public async Task PostSimulador_WithUnknownTituloId_ShouldReturn404()
    {
        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = Guid.NewGuid(),
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 17. POST /simulador ValorInvestido<=0 -> 400
    [Fact]
    public async Task PostSimulador_WithNonPositiveValorInvestido_ShouldReturn400()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = tituloId,
            ValorInvestido = 0m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 18. POST /simulador/cenarios 2 cenários com ProjecaoAnual -> 200 coleção
    [Fact]
    public async Task PostSimuladorCenarios_WithTwoScenarios_ShouldReturn200Collection()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2035, 5, 15));

        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
        {
            TituloId = tituloId,
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
        var dtos = await response.Content.ReadFromJsonAsync<List<CenarioResultadoDto>>(cancellationToken: CancellationToken.None);
        dtos.Should().HaveCount(2);
        dtos.Should().Contain(c => c.Nome == "Otimista");
        dtos.Should().Contain(c => c.Nome == "Pessimista");
    }

    // 19. POST /simulador/cenarios TituloId inexistente -> 404 (Titulo.NotFound via ResultExtensions.ToHttpResult)
    [Fact]
    public async Task PostSimuladorCenarios_WithUnknownTituloId_ShouldReturn404()
    {
        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
        {
            TituloId = Guid.NewGuid(),
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            Cenarios = new[]
            {
                new { Nome = "Otimista", ProjecaoAnual = 3.5m }
            }
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Tarefa 11 — cache + fallback do BCB Focus. Testes abaixo controlam a resposta
    // simulada do BCB via factory.BcbResponder (ver ApiTestFactory.ConfigureWebHost).

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

    private const string EmptyValueJson = """{"value": []}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // 20. BCB responde {"value":[]} -> 404 application/problem+json (buraco herdado da tarefa 7)
    [Fact]
    public async Task PostSimulador_WhenBcbReturnsEmptyValue_ShouldReturn404ProblemJson()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2030, 1, 1));
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson);

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // 21. BCB responde 500 com cache frio -> 400 application/problem+json
    [Fact]
    public async Task PostSimulador_WhenBcbFailsAndCacheIsCold_ShouldReturn400ProblemJson()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2032, 1, 1));
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.InternalServerError, string.Empty);

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // 22. 1ª chamada OK, BCB cai, 2ª chamada após o TTL (2s no host de teste) -> 200 com
    // ProjecaoUtilizada.Origem == CacheFallback
    [Fact]
    public async Task PostSimulador_WhenBcbFailsAfterTtlExpires_ShouldFallBackToLastKnownGood()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2033, 1, 1));

        object RequestBody() => new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        };

        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, SelicSuccessJson);

        var firstResponse = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstDto = await firstResponse.Content
            .ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        firstDto!.ProjecaoUtilizada.Should().NotBeNull();
        firstDto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.Bcb);

        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.InternalServerError, string.Empty);

        // FocusBcb:CacheTtl é encurtado para 2s só no host de teste (ver ApiTestFactory).
        // Avança o relógio fake da fixture em vez de Task.Delay real — determinístico.
        factory.AdvanceTime(TimeSpan.FromSeconds(3));

        var secondResponse = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondDto = await secondResponse.Content
            .ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        secondDto!.ProjecaoUtilizada.Should().NotBeNull();
        secondDto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.CacheFallback);
        secondDto.ProjecaoUtilizada!.ValorAnual.Should().Be(firstDto.ProjecaoUtilizada!.ValorAnual);
    }

    // 23. 1ª chamada OK (aquece fresh 2s + lkg 7d), avança 3s (expira fresh), BCB responde
    // 200 sem dados -> Projecao.NotFound propaga como 404 problem+json, SEM servir o lkg
    // quente (fallback só cobre HttpError — ver CachedProjecaoMercadoService).
    [Fact]
    public async Task PostSimulador_WhenBcbReturnsEmptyValueWithHotLkg_ShouldReturn404WithoutServingFallback()
    {
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2031, 1, 1));
        object RequestBody() => new
        {
            TituloId = tituloId,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        };

        // Aquece fresh (2s) + lkg (7d) com resposta válida do BCB.
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, SelicSuccessJson);
        var warm = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);
        warm.StatusCode.Should().Be(HttpStatusCode.OK);

        // Expira fresh (2s), mantém lkg (7d) — determinístico, sem Task.Delay.
        factory.AdvanceTime(TimeSpan.FromSeconds(3));

        // BCB responde 200 sem dados -> Projecao.NotFound (NÃO é HttpError).
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson);
        var response = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);

        // NotFound propaga como 404 problem+json; lkg NÃO é servido (fallback só p/ HttpError).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
