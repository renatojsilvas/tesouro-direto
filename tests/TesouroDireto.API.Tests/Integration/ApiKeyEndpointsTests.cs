using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using TesouroDireto.API.Contracts;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class ApiKeyEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string ActingUserSubHeader = "X-Acting-User-Sub";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Usuario> SeedUsuarioAsync(string email, string googleSub, bool aprovado)
    {
        Usuario? criado = null;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var usuario = Usuario.Create(
                Email.Create(email).Value,
                "Usuario Teste",
                PapelUsuario.User,
                new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
                googleSub).Value;

            if (aprovado)
            {
                usuario.Aprovar(usuario.Id, new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
            }

            await db.Usuarios.AddAsync(usuario);
            await db.SaveChangesAsync();

            criado = usuario;
        });

        return criado!;
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
    public async Task PostThenGet_ShouldReturn201WithChave_AndListNeverExposesRawChave()
    {
        var usuario = await SeedUsuarioAsync("dono-keys@exemplo.com", "sub-dono-keys", aprovado: true);

        using var createRequest = BuildRequest(HttpMethod.Post, "/v1/me/keys", usuario.GoogleSub);
        createRequest.Content = JsonContent.Create(new CreateApiKeyRequest("Minha Integração"));
        var createResponse = await _client.SendAsync(createRequest, CancellationToken.None);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var gerada = await createResponse.Content.ReadFromJsonAsync<GeneratedApiKeyDto>(cancellationToken: CancellationToken.None);
        gerada!.Chave.Should().NotBeNullOrWhiteSpace();
        gerada.Nome.Should().Be("Minha Integração");

        using var listRequest = BuildRequest(HttpMethod.Get, "/v1/me/keys", usuario.GoogleSub);
        var listResponse = await _client.SendAsync(listRequest, CancellationToken.None);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = await listResponse.Content.ReadAsStringAsync(CancellationToken.None);
        listBody.Should().NotContain(gerada.Chave);

        var lista = await listResponse.Content.ReadFromJsonAsync<List<ApiKeyDto>>(cancellationToken: CancellationToken.None);
        lista.Should().ContainSingle(k => k.Id == gerada.Id && k.Prefixo == gerada.Prefixo && k.Nome == gerada.Nome);
    }

    [Fact]
    public async Task GeneratedKey_ShouldAuthorizePublicEndpoint_ThenLoseAccessAfterRevoke()
    {
        var usuario = await SeedUsuarioAsync("dono-nao-vacuo@exemplo.com", "sub-dono-nao-vacuo", aprovado: true);

        using var createRequest = BuildRequest(HttpMethod.Post, "/v1/me/keys", usuario.GoogleSub);
        createRequest.Content = JsonContent.Create(new CreateApiKeyRequest("Cliente Publico"));
        var createResponse = await _client.SendAsync(createRequest, CancellationToken.None);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var gerada = (await createResponse.Content.ReadFromJsonAsync<GeneratedApiKeyDto>(cancellationToken: CancellationToken.None))!;

        using var publicClient = factory.CreateClient();
        publicClient.DefaultRequestHeaders.Add(ApiTestFactory.ApiKeyHeader, gerada.Chave);

        var beforeRevoke = await publicClient.GetAsync("/v1/titulos", CancellationToken.None);
        beforeRevoke.StatusCode.Should().Be(HttpStatusCode.OK);

        using var revokeRequest = BuildRequest(HttpMethod.Post, $"/v1/me/keys/{gerada.Id}/revogar", usuario.GoogleSub);
        var revokeResponse = await _client.SendAsync(revokeRequest, CancellationToken.None);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRevoke = await publicClient.GetAsync("/v1/titulos", CancellationToken.None);
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Usuario_ShouldNotSeeOrRevokeKeysOfAnotherUsuario()
    {
        var usuarioA = await SeedUsuarioAsync("dono-a@exemplo.com", "sub-dono-a", aprovado: true);
        var usuarioB = await SeedUsuarioAsync("dono-b@exemplo.com", "sub-dono-b", aprovado: true);

        using var createRequest = BuildRequest(HttpMethod.Post, "/v1/me/keys", usuarioA.GoogleSub);
        createRequest.Content = JsonContent.Create(new CreateApiKeyRequest("Key do A"));
        var createResponse = await _client.SendAsync(createRequest, CancellationToken.None);
        var geradaDeA = (await createResponse.Content.ReadFromJsonAsync<GeneratedApiKeyDto>(cancellationToken: CancellationToken.None))!;

        using var listComB = BuildRequest(HttpMethod.Get, "/v1/me/keys", usuarioB.GoogleSub);
        var listResponseB = await _client.SendAsync(listComB, CancellationToken.None);
        listResponseB.StatusCode.Should().Be(HttpStatusCode.OK);
        var listaB = await listResponseB.Content.ReadFromJsonAsync<List<ApiKeyDto>>(cancellationToken: CancellationToken.None);
        listaB.Should().NotContain(k => k.Id == geradaDeA.Id);

        using var revokeComB = BuildRequest(HttpMethod.Post, $"/v1/me/keys/{geradaDeA.Id}/revogar", usuarioB.GoogleSub);
        var revokeResponseB = await _client.SendAsync(revokeComB, CancellationToken.None);
        revokeResponseB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Usuario_NaoAprovado_ShouldReceive403OnGenerate()
    {
        var pendente = await SeedUsuarioAsync("pendente-keys@exemplo.com", "sub-pendente-keys", aprovado: false);

        using var createRequest = BuildRequest(HttpMethod.Post, "/v1/me/keys", pendente.GoogleSub);
        createRequest.Content = JsonContent.Create(new CreateApiKeyRequest("Tentativa"));
        var response = await _client.SendAsync(createRequest, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);
    }
}
