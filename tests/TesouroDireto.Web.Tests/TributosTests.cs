using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class TributosTests : TestContext
{
    private const string TributosVazioJson = "[]";

    private const string TributoId = "22222222-2222-2222-2222-222222222222";

    private const string TributosComItemJson =
        $$"""
        [{"id":"{{TributoId}}","nome":"IOF","baseCalculo":"Rendimento","tipoCalculo":"TabelaDiaria","faixas":[],"ativo":true,"ordem":1,"cumulativo":false}]
        """;

    private FakeHttpMessageHandler ConfigureApi(
        string getResponseJson,
        Func<HttpRequestMessage, HttpResponseMessage>? postResponder = null,
        Func<HttpRequestMessage, HttpResponseMessage>? putResponder = null)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/configuracoes/tributos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, getResponseJson));

        if (postResponder is not null)
        {
            handler.When(HttpMethod.Post, "v1/configuracoes/tributos", postResponder);
        }

        if (putResponder is not null)
        {
            handler.When(HttpMethod.Put, $"v1/configuracoes/tributos/{TributoId}", putResponder);
        }

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
        return handler;
    }

    [Fact]
    public void CarregamentoInicial_QuandoApiDevolveLista_RenderizaTabelaDeTributos()
    {
        ConfigureApi(TributosComItemJson);

        var cut = RenderComponent<Tributos>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=tabela-tributos]").TextContent.Should().Contain("IOF"));
    }

    [Fact]
    public void SalvarCriacao_QuandoApiDevolveSucesso_ExibeMensagemDeSucesso()
    {
        ConfigureApi(
            TributosVazioJson,
            _ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "{}"));

        var cut = RenderComponent<Tributos>();
        cut.WaitForAssertion(() => cut.Find("#novo-tributo").Should().NotBeNull());

        cut.Find("#novo-tributo").Click();
        cut.Find("#nome").Change("IOF");
        cut.Find("[data-testid=btn-salvar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=sucesso]").TextContent.Should().Contain("Tributo criado."));
    }

    [Fact]
    public void SalvarCriacao_QuandoApiDevolveErro_ExibeAlertaDeErro()
    {
        ConfigureApi(
            TributosVazioJson,
            _ => FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.BadRequest, """{"code":"Tributo.Invalido","detail":"Nome ja existe"}"""));

        var cut = RenderComponent<Tributos>();
        cut.WaitForAssertion(() => cut.Find("#novo-tributo").Should().NotBeNull());

        cut.Find("#novo-tributo").Click();
        cut.Find("#nome").Change("IOF");
        cut.Find("[data-testid=btn-salvar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".alert-danger").TextContent.Should().Contain("Nome ja existe"));
    }

    [Fact]
    public void SalvarEdicao_QuandoApiDevolveSucesso_EnviaPutParaConfiguracoesTributosDoId()
    {
        HttpRequestMessage? requisicaoCapturada = null;
        ConfigureApi(
            TributosComItemJson,
            putResponder: request =>
            {
                requisicaoCapturada = request;
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "{}");
            });

        var cut = RenderComponent<Tributos>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=btn-editar]").Should().NotBeNull());

        cut.Find("[data-testid=btn-editar]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=btn-salvar]").Should().NotBeNull());
        cut.Find("[data-testid=btn-salvar]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=sucesso]").TextContent.Should().Contain("Tributo atualizado."));

        requisicaoCapturada.Should().NotBeNull();
        requisicaoCapturada!.Method.Should().Be(HttpMethod.Put);
        requisicaoCapturada.RequestUri!.AbsolutePath.Trim('/').Should().Be($"v1/configuracoes/tributos/{TributoId}");
    }
}
