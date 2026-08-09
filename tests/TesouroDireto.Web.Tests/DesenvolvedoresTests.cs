using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class DesenvolvedoresTests : TestContext
{
    private const string KeysVazioJson = "[]";

    private const string KeysComItemJson =
        """
        [{"id":"33333333-3333-3333-3333-333333333333","nome":"Minha Key","prefixo":"tk_abc123","ativa":true,"criadaEm":"2026-08-01T10:00:00Z","ultimoUsoEm":null}]
        """;

    private void AutorizarUsuarioAprovado()
    {
        this.AddTestAuthorization()
            .SetAuthorized("user@x")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-123"));

        Services.AddScoped<IActingUserContext, ActingUserContext>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void ConfigureApi(HttpStatusCode getStatusCode, string getResponseJson)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/me/keys", FakeHttpMessageHandler.JsonResponse(getStatusCode, getResponseJson));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
    }

    [Fact]
    public void CarregamentoInicial_QuandoUsuarioAprovado_MostraTabelaDeKeys()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(HttpStatusCode.OK, KeysComItemJson);

        var cut = RenderComponent<Desenvolvedores>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=tabela-keys]").TextContent.Should().Contain("Minha Key");
        });
    }

    [Fact]
    public void CarregamentoInicial_QuandoUsuarioPendente_ExibeAguardandoSemBotaoGerar()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(HttpStatusCode.Forbidden, """{"code":"Usuario.NaoAprovado","detail":"Usuario nao aprovado"}""");

        var cut = RenderComponent<Desenvolvedores>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=aguardando]").Should().NotBeNull();
            cut.FindAll("[data-testid=btn-gerar]").Should().BeEmpty();
        });
    }

    [Fact]
    public void CarregamentoInicial_MostraLinkParaDocumentacaoPublica()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(HttpStatusCode.OK, KeysVazioJson);

        var cut = RenderComponent<Desenvolvedores>();

        var link = cut.Find("[data-testid=link-docs]");
        link.GetAttribute("href").Should().Be("/docs");
    }
}
