using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ApiKeyMiddlewareIntegrationTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Request_WithActiveClientApiKey_ShouldReturn200()
    {
        const string rawKey = "integration-cliente-ativo";
        await SeedApiKeyAsync(rawKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, rawKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithServiceKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ApiTestFactory.ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithUnknownApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, "chave-nunca-cadastrada");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithApiKeyRevokedBeforeFirstLookup_ShouldReturn401OnFirstAndSecondAttempt()
    {
        const string rawKey = "integration-cliente-revogado-antes-do-consumo";
        var apiKeyId = await SeedApiKeyAsync(rawKey);
        await RevogarAsync(apiKeyId);

        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        firstRequest.Headers.Add(ApiKeyHeader, rawKey);
        var firstResponse = await _client.SendAsync(firstRequest, CancellationToken.None);

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        secondRequest.Headers.Add(ApiKeyHeader, rawKey);
        var secondResponse = await _client.SendAsync(secondRequest, CancellationToken.None);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedApiKeyAsync(string rawKey)
    {
        var apiKeyId = Guid.Empty;

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

            apiKeyId = apiKey.Id;
        });

        return apiKeyId;
    }

    private async Task RevogarAsync(Guid apiKeyId)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var apiKey = await db.ApiKeys.FirstAsync(k => k.Id == apiKeyId);

            apiKey.Revogar(new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero));

            await db.SaveChangesAsync();
        });
    }
}
