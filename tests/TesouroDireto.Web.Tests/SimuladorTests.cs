using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class SimuladorTests : TestContext
{
    private static readonly Guid TituloId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string TitulosJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","tipoTitulo":"Tesouro Selic","dataVencimento":"2029-03-01","indexador":"Selic","pagaJurosSemestrais":false,"vencido":false}]
        """;

    private FakeHttpMessageHandler ConfigureApi(Func<HttpRequestMessage, HttpResponseMessage> postSimuladorResponder)
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJson))
            .When(HttpMethod.Post, "simulador", postSimuladorResponder);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient));
        return handler;
    }

    private IRenderedComponent<Simulador> RenderComPreenchimento()
    {
        var cut = RenderComponent<Simulador>();

        cut.WaitForAssertion(() => cut.Find("#titulo").Should().NotBeNull());
        cut.Find("#titulo").Change(TituloId.ToString());
        cut.Find("#valorInvestido").Change("1000");

        return cut;
    }

    [Fact]
    public void Simular_QuandoApiDevolveErrorEstruturado_ExibeDescricaoDoErro()
    {
        ConfigureApi(_ => FakeHttpMessageHandler.JsonResponse(
            HttpStatusCode.BadRequest, """{"code":"Sim.X","description":"msg de erro X"}"""));

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
}
