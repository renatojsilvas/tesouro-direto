using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class ApiKeyMiddlewareTests : IClassFixture<ApiKeyMiddlewareTests.ApiKeyWebFactory>
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ValidApiKey = "test-api-key-12345";

    private readonly HttpClient _client;

    public ApiKeyMiddlewareTests(ApiKeyWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithInvalidApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, "wrong-key");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithValidApiKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithEmptyApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, string.Empty);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithWhitespaceApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, "   ");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_ToExcludedPath_WithoutApiKey_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_ToNonExcludedSimilarPath_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/healthz", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public sealed class ApiKeyWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = ValidApiKey,
                    ["ApiKey:ExcludedPaths:0"] = "/health",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
