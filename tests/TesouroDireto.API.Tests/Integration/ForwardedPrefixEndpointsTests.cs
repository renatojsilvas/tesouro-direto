using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ForwardedPrefixEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string Codigo = "tesouro-selic-2029-03-01";
    private const string ForwardedPrefixHeader = "X-Forwarded-Prefix";
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTituloComPrecosAsync(int quantidade)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(
                TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();

            var precos = Enumerable.Range(0, quantidade)
                .Select(i => PrecoTaxa.Create(
                    titulo.Id,
                    DataBase.Create(new DateOnly(2024, 1, 1).AddDays(i)).Value,
                    Taxa.Create(10m).Value, Taxa.Create(10m).Value,
                    PrecoUnitario.Create(100m).Value, PrecoUnitario.Create(100m).Value, PrecoUnitario.Create(100m).Value).Value)
                .ToArray();

            await db.PrecosTaxas.AddRangeAsync(precos);
            await db.SaveChangesAsync();
        });
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, params string[] forwardedPrefixValues)
    {
        var request = new HttpRequestMessage(method, url);
        if (forwardedPrefixValues.Length > 0)
        {
            request.Headers.Add(ForwardedPrefixHeader, forwardedPrefixValues);
        }

        return request;
    }

    [Fact]
    public async Task GetTitulo_ComXForwardedPrefixApi_LinksGanhamPrefixoApi()
    {
        await SeedTituloComPrecosAsync(1);

        using var request = BuildRequest(HttpMethod.Get, $"/v1/titulos/{Codigo}", "/api");
        var response = await _client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        var links = doc.RootElement.GetProperty("_links");

        links.GetProperty("self").GetProperty("href").GetString().Should().Be($"/api/v1/titulos/{Codigo}");
        links.GetProperty("precos").GetProperty("href").GetString().Should().Be($"/api/v1/titulos/{Codigo}/precos");
        links.GetProperty("preco-atual").GetProperty("href").GetString().Should().Be($"/api/v1/titulos/{Codigo}/preco-atual");
        links.GetProperty("simular").GetProperty("href").GetString().Should().Be("/api/v1/simulador");
    }

    [Fact]
    public async Task GetPrecosByCodigo_ComXForwardedPrefixApi_LinkHeaderGanhaPrefixoApiEmTodasAsRelacoes()
    {
        await SeedTituloComPrecosAsync(120);

        using var request = BuildRequest(
            HttpMethod.Get, $"/v1/titulos/{Codigo}/precos?page=2&pageSize=50", "/api");
        var response = await _client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Link", out var linkValues).Should().BeTrue();
        var link = linkValues!.Single();

        link.Should().Contain($"</api/v1/titulos/{Codigo}/precos?page=1&pageSize=50>; rel=\"first\"");
        link.Should().Contain($"</api/v1/titulos/{Codigo}/precos?page=1&pageSize=50>; rel=\"prev\"");
        link.Should().Contain($"</api/v1/titulos/{Codigo}/precos?page=3&pageSize=50>; rel=\"next\"");
        link.Should().Contain($"</api/v1/titulos/{Codigo}/precos?page=3&pageSize=50>; rel=\"last\"");
        link.Should().NotContain("</v1/titulos/");
    }

    [Fact]
    public async Task GetTitulo_SemHeader_LinksContinuamSemPrefixo()
    {
        await SeedTituloComPrecosAsync(1);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}", CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        doc.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().Be($"/v1/titulos/{Codigo}");
    }

    [Theory]
    [InlineData("/evil")]
    [InlineData("https://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("/api/../x")]
    [InlineData("")]
    public async Task GetTitulo_ComHeaderForjadoForaDaAllowlist_HeaderEhIgnorado(string valorForjado)
    {
        await SeedTituloComPrecosAsync(1);

        using var request = BuildRequest(HttpMethod.Get, $"/v1/titulos/{Codigo}", valorForjado);
        var response = await _client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        doc.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().Be($"/v1/titulos/{Codigo}");
    }

    [Fact]
    public async Task GetTitulo_ComMultiplosValoresNoHeader_HeaderEhIgnorado()
    {
        await SeedTituloComPrecosAsync(1);

        using var request = BuildRequest(HttpMethod.Get, $"/v1/titulos/{Codigo}", "/api", "/evil");
        var response = await _client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        doc.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().Be($"/v1/titulos/{Codigo}");
    }

    [Fact]
    public async Task GetTitulos_ComHeaderPresente_RoteamentoContinuaRespondendo200()
    {
        using var request = BuildRequest(HttpMethod.Get, "/v1/titulos", "/api");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
