using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class ApiKeysTests : TestContext
{
    private const string KeysVazioJson = "[]";

    private const string KeysComItemJson =
        """
        [{"id":"33333333-3333-3333-3333-333333333333","nome":"Minha Key","prefixo":"tk_abc123","ativa":true,"criadaEm":"2026-08-01T10:00:00Z","ultimoUsoEm":null}]
        """;

    private const string ChaveGeradaJson =
        """
        {"id":"44444444-4444-4444-4444-444444444444","nome":"Nova Key","prefixo":"tk_xyz999","chave":"tk_xyz999_segredo-super-secreto","criadaEm":"2026-08-08T10:00:00Z"}
        """;

    private void AutorizarUsuarioAprovado()
    {
        this.AddTestAuthorization()
            .SetAuthorized("user@x")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, "sub-123"));

        Services.AddScoped<IActingUserContext, ActingUserContext>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private FakeHttpMessageHandler ConfigureApi(
        HttpStatusCode getStatusCode,
        string getResponseJson,
        Func<HttpRequestMessage, HttpResponseMessage>? postResponder = null)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "me/keys", FakeHttpMessageHandler.JsonResponse(getStatusCode, getResponseJson));

        if (postResponder is not null)
        {
            handler.When(HttpMethod.Post, "me/keys", postResponder);
        }

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
        return handler;
    }

    [Fact]
    public void CarregamentoInicial_QuandoUsuarioAprovado_RenderizaTabelaDeKeys()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(HttpStatusCode.OK, KeysComItemJson);

        var cut = RenderComponent<ApiKeys>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=tabela-keys]").TextContent.Should().Contain("Minha Key"));
    }

    [Fact]
    public void Gerar_QuandoApiDevolveSucesso_ExibeBannerComChaveENaoNaLista()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(
            HttpStatusCode.OK,
            KeysComItemJson,
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.Created, ChaveGeradaJson));

        var cut = RenderComponent<ApiKeys>();
        cut.WaitForAssertion(() => cut.Find("#nome-key").Should().NotBeNull());

        cut.Find("#nome-key").Change("Nova Key");
        cut.Find("[data-testid=btn-gerar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=chave-gerada]").TextContent.Should().Contain("tk_xyz999_segredo-super-secreto"));

        cut.Find("[data-testid=chave-gerada]").TextContent.Should().Contain("não será mostrada novamente");
        cut.Find("[data-testid=tabela-keys]").TextContent.Should().NotContain("tk_xyz999_segredo-super-secreto");
    }

    [Fact]
    public void Revogar_QuandoApiDevolveSucesso_Recarrega()
    {
        AutorizarUsuarioAprovado();
        var handler = ConfigureApi(HttpStatusCode.OK, KeysComItemJson);
        handler.When(
            HttpMethod.Post,
            "me/keys/33333333-3333-3333-3333-333333333333/revogar",
            FakeHttpMessageHandler.NoContentResponse());

        var cut = RenderComponent<ApiKeys>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=btn-revogar]").Should().NotBeNull());

        cut.Find("[data-testid=btn-revogar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=sucesso]").TextContent.Should().Contain("API key revogada."));
    }

    [Fact]
    public void CarregamentoInicial_QuandoUsuarioPendente_ExibeAvisoDeAguardando()
    {
        AutorizarUsuarioAprovado();
        ConfigureApi(HttpStatusCode.Forbidden, """{"code":"Usuario.NaoAprovado","detail":"Usuario nao aprovado"}""");

        var cut = RenderComponent<ApiKeys>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=aguardando]").TextContent.Should().Contain("aguardando aprovação"));
    }
}
