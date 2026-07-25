using System.Net;
using System.Net.Mime;
using System.Text.Json;
using FluentAssertions;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ExceptionHandlerEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetTestThrow_ShouldReturnProblemDetailsWithCorrelationIdAndTraceId()
    {
        var response = await _client.GetAsync("/_test/throw", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("correlationId", out var correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrEmpty();

        document.RootElement.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetTestThrow_WithKnownCorrelationId_ShouldEchoItInBody()
    {
        const string knownCorrelationId = "known-correlation-id-1234567890";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/throw");
        request.Headers.Add("X-Correlation-Id", knownCorrelationId);
        request.Headers.Add(ApiTestFactory.ApiKeyHeader, ApiTestFactory.ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("correlationId").GetString().Should().Be(knownCorrelationId);
    }

    [Fact]
    public async Task GetProtectedRoute_WithoutApiKey_ShouldReturnProblemDetailsWithCorrelationId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/titulos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("correlationId", out var correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrEmpty();
    }
}
