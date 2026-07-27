using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class HateoasLinksEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string Codigo = "tesouro-selic-2029-03-01";
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTituloComPrecoAsync()
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(
                TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();

            var preco = PrecoTaxa.Create(titulo.Id, DataBase.Create(new DateOnly(2024, 1, 2)).Value,
                Taxa.Create(13.12m), Taxa.Create(13.18m),
                PrecoUnitario.Create(756.43m).Value, PrecoUnitario.Create(755.39m).Value,
                PrecoUnitario.Create(756.43m).Value).Value;
            await db.PrecosTaxas.AddAsync(preco);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GetTitulos_EachItem_ShouldExposeLinksWithSelfPrecosPrecoAtualAndSimular()
    {
        await SeedTituloComPrecoAsync();

        var response = await _client.GetAsync("/titulos", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        var item = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("codigo").GetString() == Codigo);

        var links = item.GetProperty("_links");
        links.GetProperty("self").GetProperty("href").GetString().Should().Be($"/titulos/{Codigo}");
        links.GetProperty("precos").GetProperty("href").GetString().Should().Be($"/titulos/{Codigo}/precos");
        links.GetProperty("preco-atual").GetProperty("href").GetString().Should().Be($"/titulos/{Codigo}/preco-atual");

        var simular = links.GetProperty("simular");
        simular.GetProperty("href").GetString().Should().Be("/simulador");
        simular.GetProperty("method").GetString().Should().Be("POST");
        simular.GetProperty("templated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetTitulos_FollowingPrecosLink_ShouldReturn200WithRealData()
    {
        await SeedTituloComPrecoAsync();

        var listResponse = await _client.GetAsync("/titulos", CancellationToken.None);
        var listBody = await listResponse.Content.ReadAsStringAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(listBody);
        var item = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("codigo").GetString() == Codigo);
        var precosHref = item.GetProperty("_links").GetProperty("precos").GetProperty("href").GetString();

        var precosResponse = await _client.GetAsync(precosHref, CancellationToken.None);

        precosResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var precosBody = await precosResponse.Content.ReadAsStringAsync(CancellationToken.None);
        using var precosDoc = JsonDocument.Parse(precosBody);
        precosDoc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetTitulo_BySelfLink_ShouldReturn200WithSameCodigoAndLinks()
    {
        await SeedTituloComPrecoAsync();

        var response = await _client.GetAsync($"/titulos/{Codigo}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("codigo").GetString().Should().Be(Codigo);
        doc.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().Be($"/titulos/{Codigo}");
    }

    [Fact]
    public async Task GetPrecosByCodigo_ItemsShouldNotHaveLinks()
    {
        await SeedTituloComPrecoAsync();

        var response = await _client.GetAsync($"/titulos/{Codigo}/precos", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        var item = doc.RootElement.EnumerateArray().First();
        item.TryGetProperty("_links", out _).Should().BeFalse();
    }
}
