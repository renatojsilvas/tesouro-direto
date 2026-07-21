using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SimuladorEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

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
}
