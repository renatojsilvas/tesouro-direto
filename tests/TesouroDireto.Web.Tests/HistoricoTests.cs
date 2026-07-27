using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class HistoricoTests : TestContext
{
    private const string Codigo = "tesouro-selic-2029-03-01";

    private const string TitulosJson =
        """
        [{"codigo":"tesouro-selic-2029-03-01","tipoTitulo":"Tesouro Selic","dataVencimento":"2029-03-01","indexador":"Selic","_links":{"precos":{"href":"/titulos/tesouro-selic-2029-03-01/precos"}}}]
        """;

    private const string LinkPagina1 =
        """
        </titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=25>; rel="first", </titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=25>; rel="next", </titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=25>; rel="last"
        """;

    private const string LinkPagina2 =
        """
        </titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=25>; rel="first", </titulos/tesouro-selic-2029-03-01/precos?page=1&pageSize=25>; rel="prev", </titulos/tesouro-selic-2029-03-01/precos?page=2&pageSize=25>; rel="last"
        """;

    private static string PrecosJson(string dataBase)
        => $$"""[{"dataBase":"{{dataBase}}","taxaCompra":1,"taxaVenda":1,"puCompra":100,"puVenda":100,"puBase":100}]""";

    private void ConfigureApi()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "titulos", FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, TitulosJson))
            .When(HttpMethod.Get, $"titulos/{Codigo}/precos", request =>
            {
                var ePaginaDois = request.RequestUri!.Query.Contains("page=2");

                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    ePaginaDois ? PrecosJson("2024-01-03") : PrecosJson("2024-01-02"),
                    new Dictionary<string, string>
                    {
                        ["X-Total-Count"] = "26",
                        ["Link"] = ePaginaDois ? LinkPagina2 : LinkPagina1
                    });
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new TesouroApiClient(httpClient, new BoundedConditionalGetStore()));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<Historico> RenderComTituloSelecionado()
    {
        var cut = RenderComponent<Historico>();

        cut.WaitForAssertion(() => cut.Find("#titulo").Should().NotBeNull());
        cut.Find("#titulo").Change(Codigo);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=tabela-precos]").TextContent.Should().Contain("02/01/2024"));

        return cut;
    }

    [Fact]
    public void SelecionarTitulo_CarregaPrimeiraPagina_ComPrimeiraEAnteriorDesabilitadas()
    {
        ConfigureApi();
        var cut = RenderComTituloSelecionado();

        cut.Find("[data-testid=pg-first]").GetAttribute("disabled").Should().NotBeNull();
        cut.Find("[data-testid=pg-prev]").GetAttribute("disabled").Should().NotBeNull();
        cut.Find("[data-testid=pg-next]").GetAttribute("disabled").Should().BeNull();
        cut.Find("[data-testid=pg-last]").GetAttribute("disabled").Should().BeNull();
        cut.Find("[data-testid=pg-info]").TextContent.Should().Contain("26");
    }

    [Fact]
    public void ClicarProxima_CarregaSegundaPagina_ComProximaEUltimaDesabilitadas()
    {
        ConfigureApi();
        var cut = RenderComTituloSelecionado();

        cut.Find("[data-testid=pg-next]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=tabela-precos]").TextContent.Should().Contain("03/01/2024"));

        cut.Find("[data-testid=tabela-precos]").TextContent.Should().NotContain("02/01/2024");
        cut.Find("[data-testid=pg-next]").GetAttribute("disabled").Should().NotBeNull();
        cut.Find("[data-testid=pg-last]").GetAttribute("disabled").Should().NotBeNull();
        cut.Find("[data-testid=pg-first]").GetAttribute("disabled").Should().BeNull();
        cut.Find("[data-testid=pg-prev]").GetAttribute("disabled").Should().BeNull();
    }
}
