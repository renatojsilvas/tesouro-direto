using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

public sealed class AuthFailureRateLimitIntegrationTests(AuthFailureRateLimitApiTestFactory factory)
    : IClassFixture<AuthFailureRateLimitApiTestFactory>, IAsyncLifetime
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const string InvalidKey = "chave-invalida";

    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InvalidKey_Burst_ShouldReturn401ThenBlockWith429()
    {
        const string ip = "203.0.113.5";

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(ip, InvalidKey);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await SendAsync(ip, InvalidKey);

        blocked.StatusCode.Should().Be((HttpStatusCode)429);
        blocked.Headers.RetryAfter.Should().NotBeNull();
        blocked.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ValidServiceKey_ShouldNeverBeBlocked_EvenAfterFailureFromSameIp()
    {
        const string ip = "203.0.113.9";

        var firstFailure = await SendAsync(ip, InvalidKey);
        firstFailure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        for (var i = 0; i < 5; i++)
        {
            var response = await SendAsync(ip, ApiTestFactory.ValidApiKey);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ValidClientKey_ShouldNotBeBlocked_AfterFailureFromSameIp()
    {
        const string ip = "203.0.113.15";
        const string rawKey = "auth-failure-cliente-legitimo";
        await SeedApiKeyAsync(rawKey);

        var firstFailure = await SendAsync(ip, InvalidKey);
        firstFailure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var legitimate = await SendAsync(ip, rawKey);

        legitimate.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidKeys_ShouldBeHonored_EvenFromIpThatExhaustedFailureBudget()
    {
        const string ip = "203.0.113.30";
        const string rawKey = "auth-failure-cliente-legitimo-ip-bloqueado";
        await SeedApiKeyAsync(rawKey);

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(ip, InvalidKey);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await SendAsync(ip, InvalidKey);
        blocked.StatusCode.Should().Be((HttpStatusCode)429);

        var validClient = await SendAsync(ip, rawKey);
        validClient.StatusCode.Should().Be(HttpStatusCode.OK);

        var validService = await SendAsync(ip, ApiTestFactory.ValidApiKey);
        validService.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoIps_ShouldHaveIndependentAuthFailureQuotas()
    {
        const string ipA = "203.0.113.20";
        const string ipB = "203.0.113.21";

        for (var i = 0; i < 3; i++)
        {
            var response = await SendAsync(ipA, InvalidKey);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blockedA = await SendAsync(ipA, InvalidKey);
        blockedA.StatusCode.Should().Be((HttpStatusCode)429);

        var responseB = await SendAsync(ipB, InvalidKey);
        responseB.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendAsync(string forwardedFor, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ForwardedForHeader, forwardedFor);
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
}
