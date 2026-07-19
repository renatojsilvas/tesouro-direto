using System.Net;
using FluentAssertions;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class HealthEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetHealth_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthReady_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthLive_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
