using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class PrecosPaginacaoEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string Codigo = "tesouro-selic-2029-03-01";
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

    [Fact]
    public async Task GetPrecosByCodigo_WithoutPage_ShouldReturnEntireCollectionLikeBeforePagination()
    {
        await SeedTituloComPrecosAsync(120);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().HaveCount(120);
        response.Headers.TryGetValues("X-Total-Count", out var totalCountValues).Should().BeTrue();
        totalCountValues!.Single().Should().Be("120");
        response.Headers.Contains("Link").Should().BeFalse("sem page, não há navegação para expor");
    }

    [Fact]
    public async Task GetPrecosByCodigo_OnFirstPage_ShouldExposeFirstAndNextAndLastButNotPrev()
    {
        await SeedTituloComPrecosAsync(120);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos?page=1&pageSize=50", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().HaveCount(50);

        response.Headers.TryGetValues("X-Total-Count", out var totalCountValues).Should().BeTrue();
        totalCountValues!.Single().Should().Be("120");

        response.Headers.TryGetValues("Link", out var linkValues).Should().BeTrue();
        var link = linkValues!.Single();
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=50>; rel=\"first\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=50>; rel=\"next\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=3&pageSize=50>; rel=\"last\"");
        link.Should().NotContain("rel=\"prev\"");
    }

    [Fact]
    public async Task GetPrecosByCodigo_OnLastPage_ShouldExposeFirstAndPrevAndLastButNotNext()
    {
        await SeedTituloComPrecosAsync(120);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos?page=3&pageSize=50", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().HaveCount(20);

        response.Headers.TryGetValues("X-Total-Count", out var totalCountValues).Should().BeTrue();
        totalCountValues!.Single().Should().Be("120");

        response.Headers.TryGetValues("Link", out var linkValues).Should().BeTrue();
        var link = linkValues!.Single();
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=50>; rel=\"first\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=50>; rel=\"prev\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=3&pageSize=50>; rel=\"last\"");
        link.Should().NotContain("rel=\"next\"");
    }

    [Fact]
    public async Task GetPrecosByCodigo_OnMiddlePage_ShouldExposeAllFourRels()
    {
        await SeedTituloComPrecosAsync(120);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos?page=2&pageSize=50", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().HaveCount(50);

        response.Headers.TryGetValues("Link", out var linkValues).Should().BeTrue();
        var link = linkValues!.Single();
        link.Should().Contain("rel=\"first\"");
        link.Should().Contain("rel=\"prev\"");
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("rel=\"last\"");
    }

    [Fact]
    public async Task GetPrecosByCodigo_WithPageWithoutPageSize_ShouldUseDefault100()
    {
        await SeedTituloComPrecosAsync(150);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos?page=1", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().HaveCount(100);
    }

    [Fact]
    public async Task GetPrecosByCodigo_WithPageBeyondLastPage_ShouldReturnEmptyBodyAndLinkWithoutNextAndCoherentPrevAndLast()
    {
        await SeedTituloComPrecosAsync(120);

        var response = await _client.GetAsync($"/v1/titulos/{Codigo}/precos?page=10&pageSize=50", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDto>>(cancellationToken: CancellationToken.None);
        precos.Should().BeEmpty();

        response.Headers.TryGetValues("X-Total-Count", out var totalCountValues).Should().BeTrue();
        totalCountValues!.Single().Should().Be("120");

        response.Headers.TryGetValues("Link", out var linkValues).Should().BeTrue();
        var link = linkValues!.Single();
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=50>; rel=\"first\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=50>; rel=\"prev\"");
        link.Should().Contain("</v1/titulos/tesouro-selic-2029-03-01/precos?page=3&pageSize=50>; rel=\"last\"");
        link.Should().NotContain("rel=\"next\"");
    }
}
