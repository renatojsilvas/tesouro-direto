using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class SimuladorTests : TestContext
{
    private const string Codigo = "tesouro-selic-2029-03-01";

    private const string TitulosJson =
        """
        [{"codigo":"tesouro-selic-2029-03-01","tipoTitulo":"Tesouro Selic","dataVencimento":"2029-03-01","indexador":"Selic","pagaJurosSemestrais":false,"vencido":false,"_links":{"simular":{"href":"simulador","method":"POST"}}}]
        """;

    private const string CodigoComLinkDistinto = "tesouro-prefixado-2029-01-01";

    private const string TitulosJsonComLinkDistinto =
        """
        [{"codigo":"tesouro-prefixado-2029-01-01","tipoTitulo":"Tesouro Prefixado","dataVencimento":"2029-01-01","indexador":"Prefixado","pagaJurosSemestrais":false,"vencido":false,"_links":{"simular":{"href":"sim-via-link","method":"POST"}}}]
        """;

    private const string CodigoSemLinkSimular = "tesouro-ipca-2035-05-15";

    private const string TitulosJsonSemLinkSimular =
        """
        [{"codigo":"tesouro-ipca-2035-05-15","tipoTitulo":"Tesouro IPCA+","dataVencimento":"2035-05-15","indexador":"IPCA","pagaJurosSemestrais":false,"vencido":false,"_links":{}}]
        """;

    private FakeHttpMessageHandler ConfigureApi(Func<HttpRequestMessage, HttpResponseMessage> postSimuladorResponder)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJson))
            .When(HttpMethod.Post, "simulador", postSimuladorResponder);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
        return handler;
    }

    private IRenderedComponent<Simulador> RenderComPreenchimento()
    {
        var cut = RenderComponent<Simulador>();

        cut.WaitForAssertion(() => cut.Find("#titulo").Should().NotBeNull());
        cut.Find("#titulo").Change(Codigo);
        cut.Find("#valorInvestido").Change("1000");

        return cut;
    }

    [Fact]
    public void Simular_QuandoApiDevolveErrorEstruturado_ExibeDescricaoDoErro()
    {
        ConfigureApi(_ => FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.BadRequest, """{"code":"Sim.X","detail":"msg de erro X"}"""));

        var cut = RenderComPreenchimento();
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=erro]").TextContent.Should().Contain("msg de erro X"));
    }

    [Fact]
    public void Simular_QuandoApiDevolveCorpoNaoJson_ExibeStatusCodeERawBody()
    {
        ConfigureApi(_ => FakeHttpMessageHandler.TextResponse(
            HttpStatusCode.BadRequest, "erro interno do servidor"));

        var cut = RenderComPreenchimento();
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
        {
            var texto = cut.Find("[data-testid=erro]").TextContent;
            texto.Should().Contain("HTTP 400");
            texto.Should().Contain("erro interno do servidor");
        });
    }

    [Fact]
    public void Simular_QuandoApiDevolveSucesso_ExibeResultado()
    {
        const string corpoSucesso =
            """
            {
                "valorInvestido": 1000,
                "valorBruto": 1200,
                "rendimentoBruto": 200,
                "tributosAplicados": [],
                "totalTributos": 0,
                "valorLiquido": 1200,
                "rendimentoLiquido": 200,
                "cupons": []
            }
            """;

        ConfigureApi(_ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, corpoSucesso));

        var cut = RenderComPreenchimento();
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=resultado]").Should().NotBeNull());
    }

    [Fact]
    public void Simular_QuandoApiDevolveProjecaoUtilizada_ExibeCardComOrigemEValorAnual()
    {
        const string corpoSucesso =
            """
            {
                "valorInvestido": 1000,
                "valorBruto": 1200,
                "rendimentoBruto": 200,
                "tributosAplicados": [],
                "totalTributos": 0,
                "valorLiquido": 1200,
                "rendimentoLiquido": 200,
                "cupons": [],
                "projecaoUtilizada": {
                    "valorAnual": 6.5,
                    "dataReferencia": "2026-08-01",
                    "obtidaEmUtc": "2026-08-01T10:00:00Z",
                    "origem": "Focus"
                }
            }
            """;

        ConfigureApi(_ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, corpoSucesso));

        var cut = RenderComPreenchimento();
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
        {
            var texto = cut.Find("[data-testid=projecao-utilizada]").TextContent;
            texto.Should().Contain("Focus");
            texto.Should().Contain("50");
        });
    }

    [Fact]
    public void Simular_QuandoApiNaoDevolveProjecaoUtilizada_NaoExibeCard()
    {
        const string corpoSucesso =
            """
            {
                "valorInvestido": 1000,
                "valorBruto": 1200,
                "rendimentoBruto": 200,
                "tributosAplicados": [],
                "totalTributos": 0,
                "valorLiquido": 1200,
                "rendimentoLiquido": 200,
                "cupons": []
            }
            """;

        ConfigureApi(_ => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, corpoSucesso));

        var cut = RenderComPreenchimento();
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=resultado]").Should().NotBeNull());

        cut.FindAll("[data-testid=projecao-utilizada]").Should().BeEmpty();
    }

    [Fact]
    public void Simular_QuandoLinkSimularApontaParaHrefDistinto_SeguePorEsseHrefEExibeResultado()
    {
        const string corpoSucesso =
            """
            {
                "valorInvestido": 1000,
                "valorBruto": 1200,
                "rendimentoBruto": 200,
                "tributosAplicados": [],
                "totalTributos": 0,
                "valorLiquido": 1200,
                "rendimentoLiquido": 200,
                "cupons": []
            }
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJsonComLinkDistinto))
            .When(HttpMethod.Post, "sim-via-link", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, corpoSucesso));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));

        var cut = RenderComponent<Simulador>();

        cut.WaitForAssertion(() => cut.Find("#titulo").Should().NotBeNull());
        cut.Find("#titulo").Change(CodigoComLinkDistinto);
        cut.Find("#valorInvestido").Change("1000");
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=resultado]").Should().NotBeNull());
    }

    [Fact]
    public void Simular_QuandoTituloNaoTemLinkSimular_UsaFallbackV1Simulador()
    {
        const string corpoSucesso =
            """
            {
                "valorInvestido": 1000,
                "valorBruto": 1200,
                "rendimentoBruto": 200,
                "tributosAplicados": [],
                "totalTributos": 0,
                "valorLiquido": 1200,
                "rendimentoLiquido": 200,
                "cupons": []
            }
            """;

        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "v1/titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJsonSemLinkSimular))
            .When(HttpMethod.Post, "v1/simulador", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, corpoSucesso));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));

        var cut = RenderComponent<Simulador>();

        cut.WaitForAssertion(() => cut.Find("#titulo").Should().NotBeNull());
        cut.Find("#titulo").Change(CodigoSemLinkSimular);
        cut.Find("#valorInvestido").Change("1000");
        cut.Find("#simular").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=resultado]").Should().NotBeNull());
    }
}
