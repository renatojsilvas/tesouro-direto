using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ConditionalGetEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedTituloAsync(DateOnly dataVencimento)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> SeedTituloComPrecoAsync(DateOnly dataVencimento, DateOnly dataBase)
    {
        var tituloId = Guid.Empty;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();

            var preco = PrecoTaxa.Create(titulo.Id, DataBase.Create(dataBase).Value,
                Taxa.Create(10m).Value, Taxa.Create(10m).Value,
                PrecoUnitario.Create(100m).Value, PrecoUnitario.Create(100m).Value,
                PrecoUnitario.Create(100m).Value).Value;
            await db.PrecosTaxas.AddAsync(preco);
            await db.SaveChangesAsync();

            tituloId = titulo.Id;
        });

        return tituloId;
    }

    private async Task SeedPrecoAsync(Guid tituloId, DateOnly dataBase)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var preco = PrecoTaxa.Create(tituloId, DataBase.Create(dataBase).Value,
                Taxa.Create(11m).Value, Taxa.Create(11m).Value,
                PrecoUnitario.Create(101m).Value, PrecoUnitario.Create(101m).Value,
                PrecoUnitario.Create(101m).Value).Value;
            await db.PrecosTaxas.AddAsync(preco);
            await db.SaveChangesAsync();
        });
    }

    private static HttpRequestMessage ConditionalGet(string uri, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return request;
    }

    [Fact]
    public async Task GetTitulos_FirstCallWithoutIfNoneMatch_ShouldReturn200WithEtagAndCacheControl()
    {
        await SeedTituloAsync(new DateOnly(2029, 3, 1));

        var response = await _client.GetAsync("/v1/titulos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task GetTitulos_SecondCallWithSameIfNoneMatch_ShouldReturn304()
    {
        await SeedTituloAsync(new DateOnly(2029, 3, 1));

        var first = await _client.GetAsync("/v1/titulos", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        using var conditionalRequest = ConditionalGet("/v1/titulos", etag);
        var second = await _client.SendAsync(conditionalRequest, CancellationToken.None);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        var body = await second.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTitulos_AfterDataChanges_ShouldReturnDifferentEtagAnd200EvenWithStaleIfNoneMatch()
    {
        await SeedTituloAsync(new DateOnly(2029, 3, 1));

        var first = await _client.GetAsync("/v1/titulos", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        using var stillMatching = ConditionalGet("/v1/titulos", etag);
        var stillMatchingResponse = await _client.SendAsync(stillMatching, CancellationToken.None);
        stillMatchingResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);

        await SeedTituloAsync(new DateOnly(2035, 5, 15));

        using var staleRequest = ConditionalGet("/v1/titulos", etag);
        var afterMutation = await _client.SendAsync(staleRequest, CancellationToken.None);

        afterMutation.StatusCode.Should().Be(HttpStatusCode.OK);
        afterMutation.Headers.ETag!.Tag.Should().NotBe(etag);
    }

    [Fact]
    public async Task GetTitulos_WithUnrelatedIfNoneMatch_ShouldReturn200()
    {
        await SeedTituloAsync(new DateOnly(2029, 3, 1));

        using var request = ConditionalGet("/v1/titulos", "\"nao-e-o-etag-atual\"");
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTituloByCodigo_WithUnknownCodigo_ShouldReturn404WithoutCacheControlHeader()
    {
        var response = await _client.GetAsync("/v1/titulos/tesouro-selic-2099-01-01", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public async Task GetTituloByCodigo_WithMalformedCodigo_ShouldReturn400WithoutCacheControlHeader()
    {
        var response = await _client.GetAsync("/v1/titulos/tesouro-selic-2029-13-40", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public async Task GetTituloByCodigo_UnknownCodigo_ShouldNeverExposeAnEtagHeader()
    {
        var response = await _client.GetAsync("/v1/titulos/tesouro-selic-2099-01-01", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.ETag.Should().BeNull();
    }

    [Fact]
    public async Task GetTituloByCodigo_UnknownCodigoRepeatedWithIfNoneMatchFromFirstResponse_ShouldStillReturn404Not304()
    {
        const string uri = "/v1/titulos/tesouro-selic-2099-01-01";

        var firstResponse = await _client.GetAsync(uri, CancellationToken.None);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var suppliedEtag = firstResponse.Headers.ETag?.Tag ?? "\"stale-client-cached-etag\"";

        using var secondRequest = ConditionalGet(uri, suppliedEtag);
        var secondResponse = await _client.SendAsync(secondRequest, CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTitulos_AfterInsertingNewPrecoForExistingTitulo_ShouldChangeEtagEvenThoughTituloListIsUnchanged()
    {
        var tituloId = await SeedTituloComPrecoAsync(new DateOnly(2029, 3, 1), new DateOnly(2024, 1, 2));

        var first = await _client.GetAsync("/v1/titulos", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        await SeedPrecoAsync(tituloId, new DateOnly(2024, 1, 3));

        using var staleRequest = ConditionalGet("/v1/titulos", etag);
        var afterNewPreco = await _client.SendAsync(staleRequest, CancellationToken.None);

        afterNewPreco.StatusCode.Should().Be(HttpStatusCode.OK);
        afterNewPreco.Headers.ETag!.Tag.Should().NotBe(etag);
    }
}
