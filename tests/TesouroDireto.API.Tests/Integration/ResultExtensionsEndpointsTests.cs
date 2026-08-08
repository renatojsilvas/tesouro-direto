using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using FluentAssertions;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ResultExtensionsEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PutConfiguracoesTributos_WithUnknownId_ShouldReturnCompleteProblemJsonWith404()
    {
        var response = await _client.PutAsJsonAsync(
            $"/v1/configuracoes/tributos/{Guid.NewGuid()}",
            new { Ativo = true, Faixas = Array.Empty<object>() },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("code").GetString().Should().Be("Tributo.NotFound");

        document.RootElement.TryGetProperty("correlationId", out var correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrEmpty();

        document.RootElement.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PutConfiguracoesTributos_WithUnknownId_AndKnownCorrelationId_ShouldEchoItInBody()
    {
        const string knownCorrelationId = "known-correlation-id-result-ext-404";
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/v1/configuracoes/tributos/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { Ativo = true, Faixas = Array.Empty<object>() }),
        };
        request.Headers.Add("X-Correlation-Id", knownCorrelationId);
        request.Headers.Add(ApiTestFactory.ApiKeyHeader, ApiTestFactory.ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("correlationId").GetString().Should().Be(knownCorrelationId);
    }

    [Fact]
    public async Task GetTitulos_WithUnknownIndexador_ShouldReturnCompleteProblemJsonWith400()
    {
        var response = await _client.GetAsync("/v1/titulos?indexador=XPTO", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().NotBeNullOrEmpty();

        document.RootElement.TryGetProperty("correlationId", out var correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrEmpty();
    }
}
