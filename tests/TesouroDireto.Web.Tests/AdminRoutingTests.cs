using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class AdminRoutingTests : TestContext
{
    private const string PendentesVazioJson = "[]";

    private void ConfigureApi(string getResponseJson)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "admin/usuarios", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, getResponseJson));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
    }

    [Fact]
    public void RotaAdmin_QuandoUsuarioLogadoNaoAdmin_MostraNaoAutorizadoENaoRenderizaAdmin()
    {
        this.AddTestAuthorization()
            .SetAuthorized("user@x")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-123"));
        Services.AddSingleton(new GoogleAuthAvailability(true));
        Services.AddScoped<IActingUserContext, ActingUserContext>();
        ConfigureApi(PendentesVazioJson);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin");
        var cut = RenderComponent<Routes>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=nao-autorizado]").TextContent.Should().Contain("Você precisa entrar");
            cut.FindAll("[data-testid=tabela-pendentes]").Should().BeEmpty();
            cut.Markup.Should().NotContain("Administração de Usuários");
        });
    }

    [Fact]
    public void RotaAdmin_QuandoUsuarioLogadoAdmin_RenderizaTelaAdmin()
    {
        this.AddTestAuthorization()
            .SetAuthorized("admin@x")
            .SetRoles("Admin")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-admin-1"));
        Services.AddSingleton(new GoogleAuthAvailability(true));
        Services.AddScoped<IActingUserContext, ActingUserContext>();
        ConfigureApi(PendentesVazioJson);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/admin");
        var cut = RenderComponent<Routes>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Administração de Usuários");
            cut.FindAll("[data-testid=nao-autorizado]").Should().BeEmpty();
        });
    }
}
