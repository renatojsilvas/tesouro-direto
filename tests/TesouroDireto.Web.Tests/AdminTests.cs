using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class AdminTests : TestContext
{
    private const string PendentesComItemJson =
        """
        [{"id":"55555555-5555-5555-5555-555555555555","googleSub":"sub-pendente-1","email":"pendente@teste.com","nome":"Pendente Um","criadoEm":"2026-08-01T10:00:00Z"}]
        """;

    private void AutorizarAdmin()
    {
        this.AddTestAuthorization()
            .SetAuthorized("admin@x")
            .SetRoles("Admin")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-admin-1"));

        Services.AddScoped<IActingUserContext, ActingUserContext>();
    }

    private FakeHttpMessageHandler ConfigureApi(string getResponseJson)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "admin/usuarios", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, getResponseJson));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
        return handler;
    }

    [Fact]
    public void CarregamentoInicial_QuandoAdminLogado_RenderizaTabelaDePendentes()
    {
        AutorizarAdmin();
        ConfigureApi(PendentesComItemJson);

        var cut = RenderComponent<Admin>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=tabela-pendentes]").TextContent.Should().Contain("pendente@teste.com"));
    }

    [Fact]
    public void Aprovar_QuandoApiDevolveSucesso_Recarrega()
    {
        AutorizarAdmin();
        var handler = ConfigureApi(PendentesComItemJson);
        handler.When(
            HttpMethod.Post,
            "admin/usuarios/sub-pendente-1/aprovar",
            FakeHttpMessageHandler.NoContentResponse());

        var cut = RenderComponent<Admin>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=btn-aprovar]").Should().NotBeNull());

        cut.Find("[data-testid=btn-aprovar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=sucesso]").TextContent.Should().Contain("Usuário aprovado."));
    }

    [Fact]
    public void Desativar_QuandoApiDevolveSucesso_Recarrega()
    {
        AutorizarAdmin();
        var handler = ConfigureApi(PendentesComItemJson);
        handler.When(
            HttpMethod.Post,
            "admin/usuarios/sub-pendente-1/desativar",
            FakeHttpMessageHandler.NoContentResponse());

        var cut = RenderComponent<Admin>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=btn-desativar]").Should().NotBeNull());

        cut.Find("[data-testid=btn-desativar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=sucesso]").TextContent.Should().Contain("Usuário desativado."));
    }
}
