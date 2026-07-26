using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ImportacaoEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _authenticatedClient = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostImportacao_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.PostAsync("/importacao", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostImportacaoFeriados_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.PostAsync("/importacao/feriados", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostImportacao_WithUrlNotConfigured_ShouldReturn200WithErrorCount()
    {
        var response = await _authenticatedClient.PostAsync("/importacao", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>(cancellationToken: CancellationToken.None);
        result.Should().NotBeNull();
        result!.TitulosCriados.Should().Be(0);
        result.PrecosInseridos.Should().Be(0);
        result.LinhasComErro.Should().Be(1);
    }

    [Fact]
    public async Task PostImportacaoFeriados_WithUrlNotConfigured_ShouldReturn200WithZeroResult()
    {
        var response = await _authenticatedClient.PostAsync("/importacao/feriados", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportFeriadosResult>(cancellationToken: CancellationToken.None);
        result.Should().NotBeNull();
        result!.FeriadosImportados.Should().Be(0);
        result.FeriadosIgnorados.Should().Be(0);
    }
}
