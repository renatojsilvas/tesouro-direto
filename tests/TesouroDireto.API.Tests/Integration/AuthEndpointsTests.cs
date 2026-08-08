using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class AuthEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetTitulos_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/v1/titulos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSimulador_WithInvalidApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/simulador")
        {
            Content = JsonContent.Create(new
            {
                Codigo = "tesouro-selic-2029-01-01",
                ValorInvestido = 1000m,
                DataCompra = new DateOnly(2024, 1, 2),
                TaxaContratada = 10m,
                ProjecaoAnual = (decimal?)null
            })
        };
        request.Headers.Add(ApiTestFactory.ApiKeyHeader, "wrong-key");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConfiguracoesTributos_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/v1/configuracoes/tributos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
