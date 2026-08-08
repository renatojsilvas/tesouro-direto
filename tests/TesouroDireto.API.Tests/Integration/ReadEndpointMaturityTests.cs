using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ReadEndpointMaturityTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTituloAsync()
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(
                TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task DeleteTitulos_ShouldReturn405WithAllowHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/titulos");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var allow = string.Join(",", response.Content.Headers.Allow);
        allow.Should().Contain("GET");
        allow.Should().Contain("HEAD");
        allow.Should().Contain("OPTIONS");
    }

    [Fact]
    public async Task HeadTitulos_ShouldReturn200WithoutBody()
    {
        await SeedTituloAsync();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/v1/titulos");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task OptionsTitulos_ShouldReturn204WithAllowHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/titulos");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var allow = string.Join(",", response.Content.Headers.Allow);
        allow.Should().Contain("GET");
        allow.Should().Contain("HEAD");
        allow.Should().Contain("OPTIONS");
    }

    [Fact]
    public async Task DeletePrecosByCodigo_ShouldReturn405WithAllowHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/titulos/tesouro-selic-2029-03-01/precos");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var allow = string.Join(",", response.Content.Headers.Allow);
        allow.Should().Contain("GET");
        allow.Should().Contain("HEAD");
        allow.Should().Contain("OPTIONS");
    }
}
