using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

public sealed class RateLimitIntegrationTests(RateLimitApiTestFactory factory)
    : IClassFixture<RateLimitApiTestFactory>, IAsyncLifetime
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Client_WithinLimit_ShouldReturn200_ThenExceedingLimit_ShouldReturn429WithRetryAfter()
    {
        const string rawKey = "rate-limit-cliente-a";
        await SeedApiKeyAsync(rawKey);

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(rawKey);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rejected = await SendAsync(rawKey);

        rejected.StatusCode.Should().Be((HttpStatusCode)429);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task TwoClients_ShouldHaveIndependentQuotas()
    {
        const string rawKeyA = "rate-limit-cliente-a2";
        const string rawKeyB = "rate-limit-cliente-b2";
        await SeedApiKeyAsync(rawKeyA);
        await SeedApiKeyAsync(rawKeyB);

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(rawKeyA);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rejectedA = await SendAsync(rawKeyA);
        rejectedA.StatusCode.Should().Be((HttpStatusCode)429);

        var responseB = await SendAsync(rawKeyB);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServiceKey_ShouldNotBeRateLimited()
    {
        for (var i = 0; i < 6; i++)
        {
            var response = await SendAsync(RateLimitApiTestFactory.ValidApiKey);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ExemptPath_ShouldNotBeRateLimited()
    {
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            var response = await _client.SendAsync(request, CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Rejected_ProblemDetailsBody_ShouldContainCorrelationId()
    {
        const string rawKey = "rate-limit-cliente-correlacao";
        await SeedApiKeyAsync(rawKey);

        for (var i = 0; i < 3; i++)
        {
            await SendAsync(rawKey);
        }

        var rejected = await SendAsync(rawKey);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetailsBody>();

        problem.Should().NotBeNull();
        problem!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    private async Task<HttpResponseMessage> SendAsync(string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, apiKey);
        return await _client.SendAsync(request, CancellationToken.None);
    }

    private async Task SeedApiKeyAsync(string rawKey)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var dono = Usuario.Create(
                Email.Create($"dono-{Guid.NewGuid():N}@exemplo.com").Value,
                "Dono das Keys",
                PapelUsuario.User,
                new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

            await db.Usuarios.AddAsync(dono);

            var apiKey = ApiKey.Create(
                "Sistema Parceiro",
                ApiKeyHash.FromRawKey(rawKey),
                rawKey[..Math.Min(8, rawKey.Length)],
                dono.Id,
                new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

            await db.ApiKeys.AddAsync(apiKey);
            await db.SaveChangesAsync();
        });
    }

    private sealed class ProblemDetailsBody
    {
        [JsonPropertyName("correlationId")]
        public string? CorrelationId { get; set; }
    }
}
