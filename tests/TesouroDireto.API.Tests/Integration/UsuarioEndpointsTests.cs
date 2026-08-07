using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class UsuarioEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private const string ActingUserSubHeader = "X-Acting-User-Sub";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Usuario> SeedUsuarioAsync(
        string email, string? googleSub, PapelUsuario papel, bool aprovado, bool ativo = true)
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

    private async Task RebaixarParaUserAsync(Guid usuarioId)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE usuarios SET papel = 'User' WHERE id = {usuarioId}");
        });
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
    public async Task Aprovar_ThenDesativar_ShouldUpdateStateAndPendentesListReflectsFlow()
    {
        var admin = await SeedUsuarioAsync("admin-fluxo@exemplo.com", "sub-admin-fluxo", PapelUsuario.Admin, aprovado: true);
        var pendente = await SeedUsuarioAsync("pendente-fluxo@exemplo.com", "sub-pendente-fluxo", PapelUsuario.User, aprovado: false);
        var paraDesativar = await SeedUsuarioAsync("desativar-fluxo@exemplo.com", "sub-desativar-fluxo", PapelUsuario.User, aprovado: false);

        using var listaInicialRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        var listaInicialResponse = await _client.SendAsync(listaInicialRequest, CancellationToken.None);
        listaInicialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listaInicial = (await listaInicialResponse.Content.ReadFromJsonAsync<List<UsuarioPendenteDto>>(cancellationToken: CancellationToken.None))!;
        listaInicial.Select(u => u.GoogleSub).Should().Contain([pendente.GoogleSub, paraDesativar.GoogleSub]);
        listaInicial.Select(u => u.GoogleSub).Should().NotContain(admin.GoogleSub);

        using var aprovarRequest = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{pendente.GoogleSub}/aprovar", admin.GoogleSub);
        var aprovarResponse = await _client.SendAsync(aprovarRequest, CancellationToken.None);
        aprovarResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var desativarRequest = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{paraDesativar.GoogleSub}/desativar", admin.GoogleSub);
        var desativarResponse = await _client.SendAsync(desativarRequest, CancellationToken.None);
        desativarResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aprovadoNoBanco = await db.Usuarios.FirstAsync(u => u.Id == pendente.Id, CancellationToken.None);
        aprovadoNoBanco.Aprovado.Should().BeTrue();
        aprovadoNoBanco.AprovadoPor.Should().Be(admin.Id);
        aprovadoNoBanco.AprovadoEm.Should().NotBeNull();

        var desativadoNoBanco = await db.Usuarios.FirstAsync(u => u.Id == paraDesativar.Id, CancellationToken.None);
        desativadoNoBanco.Ativo.Should().BeFalse();

        using var listaFinalRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        var listaFinalResponse = await _client.SendAsync(listaFinalRequest, CancellationToken.None);
        var listaFinal = await listaFinalResponse.Content.ReadFromJsonAsync<List<UsuarioPendenteDto>>(cancellationToken: CancellationToken.None);
        listaFinal!.Select(u => u.GoogleSub).Should().NotContain([pendente.GoogleSub, paraDesativar.GoogleSub, admin.GoogleSub]);
    }

    [Fact]
    public async Task AdminRoutes_WithNonAdminActingUser_ShouldReturn403_ButSucceedForAdmin_AndFailAfterDemotion()
    {
        var admin = await SeedUsuarioAsync("admin-naovazio@exemplo.com", "sub-admin-naovazio", PapelUsuario.Admin, aprovado: true);
        var naoAdmin = await SeedUsuarioAsync("nao-admin@exemplo.com", "sub-nao-admin", PapelUsuario.User, aprovado: true);
        var alvo = await SeedUsuarioAsync("alvo-naovazio@exemplo.com", "sub-alvo-naovazio", PapelUsuario.User, aprovado: false);

        using var pendentesComNaoAdmin = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", naoAdmin.GoogleSub);
        (await _client.SendAsync(pendentesComNaoAdmin, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var aprovarComNaoAdmin = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{alvo.GoogleSub}/aprovar", naoAdmin.GoogleSub);
        (await _client.SendAsync(aprovarComNaoAdmin, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var desativarComNaoAdmin = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{alvo.GoogleSub}/desativar", naoAdmin.GoogleSub);
        (await _client.SendAsync(desativarComNaoAdmin, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var pendentesComAdmin = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        (await _client.SendAsync(pendentesComAdmin, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var aprovarComAdmin = BuildRequest(HttpMethod.Post, $"/admin/usuarios/{alvo.GoogleSub}/aprovar", admin.GoogleSub);
        (await _client.SendAsync(aprovarComAdmin, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await RebaixarParaUserAsync(admin.Id);

        using var pendentesComAdminRebaixado = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        (await _client.SendAsync(pendentesComAdminRebaixado, CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminRoutes_WithoutActingUserSubHeader_ShouldReturn403WithProblemJson()
    {
        using var request = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", actingUserSub: null);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);
    }

    [Fact]
    public async Task AdminRoutes_WithUnapprovedAdmin_ShouldReturn403()
    {
        var adminNaoAprovado = await SeedUsuarioAsync("admin-nao-aprovado@exemplo.com", "sub-admin-nao-aprovado", PapelUsuario.Admin, aprovado: false);

        using var request = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", adminNaoAprovado.GoogleSub);
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminRoutes_WithInactiveAdmin_ShouldReturn403()
    {
        var adminInativo = await SeedUsuarioAsync("admin-inativo@exemplo.com", "sub-admin-inativo", PapelUsuario.Admin, aprovado: true, ativo: false);

        using var request = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", adminInativo.GoogleSub);
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPendentes_WithoutPendentesQueryOrFalse_ShouldReturn400()
    {
        var admin = await SeedUsuarioAsync("admin-filtro@exemplo.com", "sub-admin-filtro", PapelUsuario.Admin, aprovado: true);

        using var semQueryRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios", admin.GoogleSub);
        var semQueryResponse = await _client.SendAsync(semQueryRequest, CancellationToken.None);
        semQueryResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        semQueryResponse.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        using var pendentesFalseRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=false", admin.GoogleSub);
        var pendentesFalseResponse = await _client.SendAsync(pendentesFalseRequest, CancellationToken.None);
        pendentesFalseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var pendentesTrueRequest = BuildRequest(HttpMethod.Get, "/admin/usuarios?pendentes=true", admin.GoogleSub);
        var pendentesTrueResponse = await _client.SendAsync(pendentesTrueRequest, CancellationToken.None);
        pendentesTrueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Aprovar_WithUnknownSub_ShouldReturn404WithCode()
    {
        var admin = await SeedUsuarioAsync("admin-404@exemplo.com", "sub-admin-404", PapelUsuario.Admin, aprovado: true);

        using var request = BuildRequest(HttpMethod.Post, "/admin/usuarios/sub-nunca-existiu/aprovar", admin.GoogleSub);
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Usuario.NotFound");
    }

    [Fact]
    public async Task Desativar_WithUnknownSub_ShouldReturn404WithCode()
    {
        var admin = await SeedUsuarioAsync("admin-404-desativar@exemplo.com", "sub-admin-404-desativar", PapelUsuario.Admin, aprovado: true);

        using var request = BuildRequest(HttpMethod.Post, "/admin/usuarios/sub-nunca-existiu/desativar", admin.GoogleSub);
        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Usuario.NotFound");
    }

    [Fact]
    public async Task Sync_WithoutGoogleSubField_ShouldReturn400WithCode()
    {
        var json = """{"Email":"sem-sub@exemplo.com","Nome":"Fulano","EmailVerified":true}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/admin/usuarios/sync", content, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Usuario.GoogleSubObrigatorio");
    }

    [Fact]
    public async Task Sync_WithoutGoogleSub_AgainstSeedAdminWithNullGoogleSub_ShouldReturn400AndNotLeakAdmin()
    {
        var admin = await SeedUsuarioAsync("admin-alvo-ataque@exemplo.com", null, PapelUsuario.Admin, aprovado: true);

        var json = """{"Email":"attacker@evil.com","Nome":"X","EmailVerified":true}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/admin/usuarios/sync", content, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Usuario.GoogleSubObrigatorio");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var noBanco = await db.Usuarios.FirstAsync(u => u.Id == admin.Id, CancellationToken.None);
        noBanco.GoogleSub.Should().BeNull();
        var atacanteExiste = await db.Usuarios.AnyAsync(u => u.Email == Email.Create("attacker@evil.com").Value, CancellationToken.None);
        atacanteExiste.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_WithEmailNotVerified_ShouldReturn400WithCode()
    {
        var command = new { GoogleSub = "sub-nao-verificado", Email = "nao-verificado@exemplo.com", Nome = "Fulano", EmailVerified = false };

        var response = await _client.PostAsJsonAsync("/admin/usuarios/sync", command, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Usuario.EmailNaoVerificado");
    }

    [Fact]
    public async Task Sync_CalledTwiceSequentially_ShouldBeIdempotentWithSingleRowAndSameId()
    {
        var command = new { GoogleSub = "sub-idempotente", Email = "idempotente@exemplo.com", Nome = "Fulano", EmailVerified = true };

        var firstResponse = await _client.PostAsJsonAsync("/admin/usuarios/sync", command, CancellationToken.None);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<UsuarioSyncDto>(cancellationToken: CancellationToken.None);

        var secondResponse = await _client.PostAsJsonAsync("/admin/usuarios/sync", command, CancellationToken.None);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<UsuarioSyncDto>(cancellationToken: CancellationToken.None);

        second!.Id.Should().Be(first!.Id);
        second.Aprovado.Should().BeFalse();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linhas = await db.Usuarios.Where(u => u.GoogleSub == "sub-idempotente").ToListAsync(CancellationToken.None);
        linhas.Should().ContainSingle();
    }

    [Fact]
    public async Task Sync_WithSeedAdminEmail_ShouldLinkGoogleSubAndKeepAdminAprovado()
    {
        var admin = await SeedUsuarioAsync("admin-seed-casamento@exemplo.com", null, PapelUsuario.Admin, aprovado: true);

        var command = new
        {
            GoogleSub = "sub-google-do-admin",
            Email = "admin-seed-casamento@exemplo.com",
            Nome = "Admin",
            EmailVerified = true
        };

        var response = await _client.PostAsJsonAsync("/admin/usuarios/sync", command, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<UsuarioSyncDto>(cancellationToken: CancellationToken.None);
        dto!.Id.Should().Be(admin.Id);
        dto.Aprovado.Should().BeTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var noBanco = await db.Usuarios.FirstAsync(u => u.Id == admin.Id, CancellationToken.None);
        noBanco.GoogleSub.Should().Be("sub-google-do-admin");
        noBanco.Papel.Should().Be(PapelUsuario.Admin);
        noBanco.Aprovado.Should().BeTrue();
    }
}
