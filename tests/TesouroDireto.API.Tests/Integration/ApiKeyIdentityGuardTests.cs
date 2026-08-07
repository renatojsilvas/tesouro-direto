using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using TesouroDireto.API.Contracts;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ApiKeyIdentityGuardTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string ActingUserSubHeader = "X-Acting-User-Sub";

    private readonly HttpClient _serviceClient = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Usuario> SeedUsuarioAsync(
        string email, string googleSub, PapelUsuario papel, bool aprovado, bool ativo = true)
    {
        Usuario? criado = null;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var usuario = Usuario.Create(
                Email.Create(email).Value,
                "Usuario Teste",
                papel,
                new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
                googleSub).Value;

            if (aprovado)
            {
                usuario.Aprovar(usuario.Id, new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
            }

            if (!ativo)
            {
                usuario.Desativar();
            }

            await db.Usuarios.AddAsync(usuario);
            await db.SaveChangesAsync();

            criado = usuario;
        });

        return criado!;
    }

    private async Task<string> GerarClientKeyAsync(Usuario dono, string nome)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/me/keys")
        {
            Content = JsonContent.Create(new CreateApiKeyRequest(nome))
        };
        createRequest.Headers.Add(ActingUserSubHeader, dono.GoogleSub);

        var response = await _serviceClient.SendAsync(createRequest, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var gerada = await response.Content.ReadFromJsonAsync<GeneratedApiKeyDto>(cancellationToken: CancellationToken.None);
        return gerada!.Chave;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string? actingUserSub)
    {
        var request = new HttpRequestMessage(method, path);
        if (actingUserSub is not null)
        {
            request.Headers.Add(ActingUserSubHeader, actingUserSub);
        }

        return request;
    }

    [Fact]
    public async Task ClientKey_ImpersonatingAnotherApprovedUser_ShouldReturn403OnAllMeKeysRoutes()
    {
        var dono = await SeedUsuarioAsync("dono-client-key@exemplo.com", "sub-dono-client-key", PapelUsuario.User, aprovado: true);
        var vitima = await SeedUsuarioAsync("vitima-personificada@exemplo.com", "sub-vitima-personificada", PapelUsuario.User, aprovado: true);

        var clientKey = await GerarClientKeyAsync(dono, "Chave do Dono");

        using var attackerClient = factory.CreateClient();
        attackerClient.DefaultRequestHeaders.Add(ApiTestFactory.ApiKeyHeader, clientKey);

        using var listRequest = BuildRequest(HttpMethod.Get, "/me/keys", vitima.GoogleSub);
        var listResponse = await attackerClient.SendAsync(listRequest, CancellationToken.None);
        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var createRequest = BuildRequest(HttpMethod.Post, "/me/keys", vitima.GoogleSub);
        createRequest.Content = JsonContent.Create(new CreateApiKeyRequest("Chave Forjada"));
        var createResponse = await attackerClient.SendAsync(createRequest, CancellationToken.None);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var revokeRequest = BuildRequest(HttpMethod.Post, $"/me/keys/{Guid.NewGuid()}/revogar", vitima.GoogleSub);
        var revokeResponse = await attackerClient.SendAsync(revokeRequest, CancellationToken.None);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClientKey_ImpersonatingAdmin_ShouldReturn403OnAdminRoutes()
    {
        var dono = await SeedUsuarioAsync("dono-client-key-admin@exemplo.com", "sub-dono-client-key-admin", PapelUsuario.User, aprovado: true);
        var admin = await SeedUsuarioAsync("admin-personificado@exemplo.com", "sub-admin-personificado", PapelUsuario.Admin, aprovado: true);

        var clientKey = await GerarClientKeyAsync(dono, "Chave do Dono Admin");

        using var attackerClient = factory.CreateClient();
        attackerClient.DefaultRequestHeaders.Add(ApiTestFactory.ApiKeyHeader, clientKey);

        using var pendentesRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        var pendentesResponse = await attackerClient.SendAsync(pendentesRequest, CancellationToken.None);
        pendentesResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var aprovarRequest = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{Guid.NewGuid()}/aprovar", admin.GoogleSub);
        var aprovarResponse = await attackerClient.SendAsync(aprovarRequest, CancellationToken.None);
        aprovarResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClientKey_CallingSync_ShouldReturn403()
    {
        var dono = await SeedUsuarioAsync("dono-client-key-sync@exemplo.com", "sub-dono-client-key-sync", PapelUsuario.User, aprovado: true);
        var clientKey = await GerarClientKeyAsync(dono, "Chave do Dono Sync");

        using var attackerClient = factory.CreateClient();
        attackerClient.DefaultRequestHeaders.Add(ApiTestFactory.ApiKeyHeader, clientKey);

        var command = new { GoogleSub = "sub-forjado", Email = "forjado@exemplo.com", Nome = "Forjado", EmailVerified = true };
        var response = await attackerClient.PostAsJsonAsync("/admin/usuarios/sync", command, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServiceKey_WithoutActingUserSubHeader_ShouldReturn403OnMeKeysRoutes()
    {
        using var listRequest = BuildRequest(HttpMethod.Get, "/me/keys", actingUserSub: null);
        (await _serviceClient.SendAsync(listRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var revokeRequest = BuildRequest(HttpMethod.Post, $"/me/keys/{Guid.NewGuid()}/revogar", actingUserSub: null);
        (await _serviceClient.SendAsync(revokeRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServiceKey_WithInactiveApprovedUser_ShouldReturn403OnMeKeysRoutes()
    {
        var inativo = await SeedUsuarioAsync("inativo-me-keys@exemplo.com", "sub-inativo-me-keys", PapelUsuario.User, aprovado: true, ativo: false);

        using var listRequest = BuildRequest(HttpMethod.Get, "/me/keys", inativo.GoogleSub);
        (await _serviceClient.SendAsync(listRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var revokeRequest = BuildRequest(HttpMethod.Post, $"/me/keys/{Guid.NewGuid()}/revogar", inativo.GoogleSub);
        (await _serviceClient.SendAsync(revokeRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServiceKey_WithPendingUser_ShouldReturn403OnListAndRevokeRoutes()
    {
        var pendente = await SeedUsuarioAsync("pendente-me-keys@exemplo.com", "sub-pendente-me-keys", PapelUsuario.User, aprovado: false);

        using var listRequest = BuildRequest(HttpMethod.Get, "/me/keys", pendente.GoogleSub);
        (await _serviceClient.SendAsync(listRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var revokeRequest = BuildRequest(HttpMethod.Post, $"/me/keys/{Guid.NewGuid()}/revogar", pendente.GoogleSub);
        (await _serviceClient.SendAsync(revokeRequest, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
