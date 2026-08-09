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

public class DesenvolvedoresRoutingTests : TestContext
{
    private const string KeysVazioJson = "[]";

    private void ConfigureApi(string getResponseJson)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/me/keys", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, getResponseJson));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
    }

    [Fact]
    public void RotaDesenvolvedores_QuandoNaoAutorizado_MostraNaoAutorizadoENaoRenderizaPortal()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(true));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/desenvolvedores");
        var cut = RenderComponent<Routes>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=nao-autorizado]").TextContent.Should().Contain("Você precisa entrar");
            cut.Markup.Should().NotContain("guia-integracao");
            cut.Markup.Should().NotContain("iframe-referencia");
        });
    }

    [Fact]
    public void RotaMinhasApiKeys_QuandoUsuarioAutorizado_RedirecionaParaDesenvolvedoresERenderizaPortal()
    {
        this.AddTestAuthorization()
            .SetAuthorized("user@x")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-123"));
        Services.AddSingleton(new GoogleAuthAvailability(true));
        Services.AddScoped<IActingUserContext, ActingUserContext>();
        ConfigureApi(KeysVazioJson);

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/minhas-api-keys");
        var cut = RenderComponent<Routes>();

        cut.WaitForAssertion(() =>
        {
            navigation.Uri.Should().EndWith("/desenvolvedores");
            cut.Find("[data-testid=painel-credenciais]").Should().NotBeNull();
        });
    }
}
