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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    public async Task PostSimulador_WithPrefixadoTitulo_ShouldReturn200()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
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

    [Fact]
    public async Task PostSimulador_ShouldSetCacheControlNoStore()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task PostSimuladorCenarios_ShouldSetCacheControlNoStore()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            Cenarios = new[] { new { Nome = "Base", ProjecaoAnual = 5m } }
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task PostSimulador_WithIndexedTituloAndExplicitProjecao_ShouldReturn200WithoutBcbCall()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2035, 5, 15));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
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

    [Fact]
    public async Task PostSimulador_WithUnknownWellFormedCodigo_ShouldReturn404()
    {
        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = "tesouro-selic-2099-01-01",
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostSimulador_WithMalformedCodigo_ShouldReturn400()
    {
        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = "nao-e-um-codigo-valido",
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("Titulo.InvalidCodigo");
    }

    [Fact]
    public async Task PostSimulador_WithNonPositiveValorInvestido_ShouldReturn400()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2029, 1, 1));

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
            ValorInvestido = 0m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSimuladorCenarios_WithTwoScenarios_ShouldReturn200Collection()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2035, 5, 15));

        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
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
        var dtos = await response.Content.ReadFromJsonAsync<List<CenarioResultadoDto>>(cancellationToken: CancellationToken.None);
        dtos.Should().HaveCount(2);
        dtos.Should().Contain(c => c.Nome == "Otimista");
        dtos.Should().Contain(c => c.Nome == "Pessimista");
    }

    [Fact]
    public async Task PostSimuladorCenarios_WithUnknownWellFormedCodigo_ShouldReturn404()
    {
        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
        {
            Codigo = "tesouro-selic-2099-01-01",
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

    [Fact]
    public async Task PostSimuladorCenarios_WithMalformedCodigo_ShouldReturn400()
    {
        var response = await _client.PostAsJsonAsync("/simulador/cenarios", new
        {
            Codigo = "nao-e-um-codigo-valido",
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            Cenarios = new[]
            {
                new { Nome = "Otimista", ProjecaoAnual = 3.5m }
            }
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("Titulo.InvalidCodigo");
    }


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

    [Fact]
    public async Task PostSimulador_WhenBcbReturnsEmptyValue_ShouldReturn404ProblemJson()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2030, 1, 1));
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson);

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostSimulador_WhenBcbFailsAndCacheIsCold_ShouldReturn400ProblemJson()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2032, 1, 1));
        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.InternalServerError, string.Empty);

        var response = await _client.PostAsJsonAsync("/simulador", new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 6m,
            ProjecaoAnual = (decimal?)null
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostSimulador_WhenBcbFailsAfterTtlExpires_ShouldFallBackToLastKnownGood()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2033, 1, 1));

        object RequestBody() => new
        {
            Codigo = codigo,
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

        factory.AdvanceTime(TimeSpan.FromSeconds(3));

        var secondResponse = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondDto = await secondResponse.Content
            .ReadFromJsonAsync<SimulacaoResultadoDto>(JsonOptions, CancellationToken.None);
        secondDto!.ProjecaoUtilizada.Should().NotBeNull();
        secondDto.ProjecaoUtilizada!.Origem.Should().Be(OrigemProjecao.CacheFallback);
        secondDto.ProjecaoUtilizada!.ValorAnual.Should().Be(firstDto.ProjecaoUtilizada!.ValorAnual);
    }

    [Fact]
    public async Task PostSimulador_WhenBcbReturnsEmptyValueWithHotLkg_ShouldReturn404WithoutServingFallback()
    {
        var codigo = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2031, 1, 1));
        object RequestBody() => new
        {
            Codigo = codigo,
            ValorInvestido = 1000m,
            DataCompra = new DateOnly(2024, 1, 2),
            TaxaContratada = 10m,
            ProjecaoAnual = (decimal?)null
        };

        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, SelicSuccessJson);
        var warm = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);
        warm.StatusCode.Should().Be(HttpStatusCode.OK);

        factory.AdvanceTime(TimeSpan.FromSeconds(3));

        factory.BcbResponder = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson);
        var response = await _client.PostAsJsonAsync("/simulador", RequestBody(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
