using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class TitulosEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<IReadOnlyDictionary<string, Guid>> SeedTitulosAsync()
    {
        var ids = new Dictionary<string, Guid>();

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var selic = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
            var ipca = Titulo.Create(TipoTitulo.TesouroIPCA, DataVencimento.Create(new DateOnly(2035, 5, 15)).Value).Value;
            var prefixadoVencido = Titulo.Create(TipoTitulo.TesouroPrefixado, DataVencimento.Create(new DateOnly(2020, 1, 1)).Value).Value;
            var ipcaComJuros = Titulo.Create(TipoTitulo.TesouroIPCAComJuros, DataVencimento.Create(new DateOnly(2040, 8, 15)).Value).Value;

            await db.Titulos.AddRangeAsync(selic, ipca, prefixadoVencido, ipcaComJuros);
            await db.SaveChangesAsync();

            ids["Selic"] = selic.Id;
            ids["IPCA"] = ipca.Id;
            ids["PrefixadoVencido"] = prefixadoVencido.Id;
            ids["IPCAComJuros"] = ipcaComJuros.Id;
        });

        return ids;
    }

    private async Task SeedPrecosAsync(Guid tituloId)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var precos = new[]
            {
                PrecoTaxa.Create(tituloId, DataBase.Create(new DateOnly(2024, 1, 2)).Value,
                    Taxa.Create(13.12m), Taxa.Create(13.18m),
                    PrecoUnitario.Create(756.43m).Value, PrecoUnitario.Create(755.39m).Value,
                    PrecoUnitario.Create(756.43m).Value).Value,
                PrecoTaxa.Create(tituloId, DataBase.Create(new DateOnly(2024, 6, 15)).Value,
                    Taxa.Create(10.50m), Taxa.Create(10.75m),
                    PrecoUnitario.Create(800.00m).Value, PrecoUnitario.Create(799.00m).Value,
                    PrecoUnitario.Create(798.00m).Value).Value,
                PrecoTaxa.Create(tituloId, DataBase.Create(new DateOnly(2024, 12, 20)).Value,
                    Taxa.Create(11.00m), Taxa.Create(11.25m),
                    PrecoUnitario.Create(850.00m).Value, PrecoUnitario.Create(849.00m).Value,
                    PrecoUnitario.Create(848.00m).Value).Value,
            };

            await db.PrecosTaxas.AddRangeAsync(precos);
            await db.SaveChangesAsync();
        });
    }

    // 1. GET /titulos sem filtro -> 200, 4 itens
    [Fact]
    public async Task GetTitulos_WithoutFilter_ShouldReturnAllTitulos()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var titulos = await response.Content.ReadFromJsonAsync<List<TituloDto>>(JsonOptions, CancellationToken.None);
        titulos.Should().HaveCount(4);
    }

    // 2. GET /titulos?indexador=IPCA -> 200, só IPCA
    [Fact]
    public async Task GetTitulos_WithIndexadorFilter_ShouldReturnOnlyMatching()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos?indexador=IPCA", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var titulos = await response.Content.ReadFromJsonAsync<List<TituloDto>>(JsonOptions, CancellationToken.None);
        titulos.Should().HaveCount(2);
        titulos.Should().AllSatisfy(t => t.Indexador.Should().Be("IPCA"));
    }

    // 3. GET /titulos?vencido=true -> 200, só vencidos
    [Fact]
    public async Task GetTitulos_WithVencidoFilter_ShouldReturnOnlyExpired()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos?vencido=true", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var titulos = await response.Content.ReadFromJsonAsync<List<TituloDto>>(JsonOptions, CancellationToken.None);
        titulos.Should().HaveCount(1);
        titulos![0].Vencido.Should().BeTrue();
    }

    // 4. GET /titulos?indexador=XPTO (inválido) -> comportamento real: 400
    [Fact]
    public async Task GetTitulos_WithInvalidIndexador_ShouldReturn400()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos?indexador=XPTO", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 5. GET /titulos/{id}/precos com preços -> 200 lista
    [Fact]
    public async Task GetPrecos_WithPrecos_ShouldReturn200List()
    {
        var ids = await SeedTitulosAsync();
        await SeedPrecosAsync(ids["Selic"]);

        var response = await _client.GetAsync($"/titulos/{ids["Selic"]}/precos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    // 6. GET /titulos/{id}/precos?dataInicio=&dataFim= -> 200 subconjunto
    [Fact]
    public async Task GetPrecos_WithDateRange_ShouldReturnSubset()
    {
        var ids = await SeedTitulosAsync();
        await SeedPrecosAsync(ids["Selic"]);

        var response = await _client.GetAsync(
            $"/titulos/{ids["Selic"]}/precos?dataInicio=2024-06-01&dataFim=2024-06-30", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    // 7. GET /titulos/{id}/precos id inexistente -> 404
    [Fact]
    public async Task GetPrecos_WithUnknownId_ShouldReturn404()
    {
        var response = await _client.GetAsync($"/titulos/{Guid.NewGuid()}/precos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 8. GET /titulos/{id}/preco-atual id com preço -> 200
    [Fact]
    public async Task GetPrecoAtual_WithPreco_ShouldReturn200()
    {
        var ids = await SeedTitulosAsync();
        await SeedPrecosAsync(ids["Selic"]);

        var response = await _client.GetAsync($"/titulos/{ids["Selic"]}/preco-atual", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 9. GET /titulos/{id}/preco-atual id sem preço -> 404
    [Fact]
    public async Task GetPrecoAtual_WithoutPreco_ShouldReturn404()
    {
        var ids = await SeedTitulosAsync();

        var response = await _client.GetAsync($"/titulos/{ids["IPCA"]}/preco-atual", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 10. GET /titulos/preco-atual?nome= nome válido -> 200
    [Fact]
    public async Task GetPrecoAtualByNome_WithValidNome_ShouldReturn200()
    {
        var ids = await SeedTitulosAsync();
        await SeedPrecosAsync(ids["Selic"]);

        var response = await _client.GetAsync("/titulos/preco-atual?nome=Tesouro Selic 2029", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 11. GET /titulos/preco-atual?nome= nome inválido -> 404
    [Fact]
    public async Task GetPrecoAtualByNome_WithUnknownNome_ShouldReturn404()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos/preco-atual?nome=Tesouro Inexistente 2099", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 12. GET /titulos/precos?nome= válido -> 200 lista
    [Fact]
    public async Task GetPrecosByNome_WithValidNome_ShouldReturn200List()
    {
        var ids = await SeedTitulosAsync();
        await SeedPrecosAsync(ids["Selic"]);

        var response = await _client.GetAsync("/titulos/precos?nome=Tesouro Selic 2029", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    // 13. GET /titulos/precos?nome= inexistente -> 404
    [Fact]
    public async Task GetPrecosByNome_WithUnknownNome_ShouldReturn404()
    {
        await SeedTitulosAsync();

        var response = await _client.GetAsync("/titulos/precos?nome=Tesouro Inexistente 2099", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
