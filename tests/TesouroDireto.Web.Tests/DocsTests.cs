using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components;
using TesouroDireto.Web.Components.Pages;
using TesouroDireto.Web.Components.Shared;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class DocsTests : TestContext
{
    private const string BaseUrlConfigurado = "https://dadosdotesourodireto.com.br/api/v1";

    private static IConfiguration BuildConfiguracao(string baseUrl = BaseUrlConfigurado) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSettings:PublicBaseUrl"] = baseUrl
            })
            .Build();

    private IRenderedComponent<Docs> RenderDocs(string baseUrl = BaseUrlConfigurado)
    {
        Services.AddSingleton(BuildConfiguracao(baseUrl));
        return RenderComponent<Docs>();
    }

    [Fact]
    public void Render_AvisoDeLinksRelativos_ExplicaQueOHrefJaTrazOSegmentoApi()
    {
        var cut = RenderDocs();

        var aviso = cut.Find("[data-testid=aviso-links-relativos]").TextContent;

        aviso.Should().Contain("/api");
        aviso.Should().Contain("https://dadosdotesourodireto.com.br/api/v1/titulos/tesouro-selic-2029-03-01");
        aviso.Should().NotContain("https://dadosdotesourodireto.com.br/api/v1/v1/");
        aviso.Should().NotContain("/api/v1/v1/");
        aviso.Should().NotContain("/v1/v1/");
    }

    [Fact]
    public void Render_QuandoPublicBaseUrlEhInvalida_NaoDerrubaAPagina()
    {
        var cut = RenderDocs("nao-e-uma-url");

        cut.Find("[data-testid=docs-nav]").Should().NotBeNull();
        cut.Find("[data-testid=aviso-links-relativos]").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RotaDocs_QuandoNaoAutorizado_RenderizaSemPedirLoginENaoMostraNaoAutorizado()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(true));
        Services.AddSingleton(BuildConfiguracao());

        Services.GetRequiredService<NavigationManager>().NavigateTo("/docs");
        var cut = RenderComponent<Routes>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=nao-autorizado]").Should().BeEmpty();
            cut.FindAll("[data-testid=docs-nav]").Should().NotBeEmpty();
        });
    }

    [Fact]
    public void Render_NavLateral_TodosOsHrefsApontamParaIdsExistentesNoDocumento()
    {
        var cut = RenderDocs();

        var links = cut.FindAll("[data-testid=docs-nav] a");
        links.Should().NotBeEmpty();

        foreach (var link in links)
        {
            var href = link.GetAttribute("href");
            href.Should().NotBeNullOrEmpty();
            href.Should().StartWith("#");

            var id = href![1..];
            cut.Find($"#{id}").Should().NotBeNull();
        }
    }

    [Fact]
    public void Render_ExemplosDePaginacao_UsamPageEPageSizeENaoPaginaETamanho()
    {
        var cut = RenderDocs();

        var markup = cut.Markup;

        markup.Should().Contain("page=");
        markup.Should().Contain("pageSize=");
        markup.Should().NotContain("pagina=");
        markup.Should().NotContain("tamanho=");
    }

    [Fact]
    public void Render_BaseUrlVemDaConfiguracao_MarkupContemValorConfigurado()
    {
        const string baseUrlEsperada = "https://api.teste.exemplo/v1";

        var cut = RenderDocs(baseUrlEsperada);

        cut.Markup.Should().Contain(baseUrlEsperada);
    }

    [Fact]
    public void Render_EndpointsDeManutencao_NaoAparecemNoMarkup()
    {
        var cut = RenderDocs();

        cut.Markup.Should().NotContain("/importacao");
        cut.Markup.Should().NotContain("PUT");
    }

    [Fact]
    public void Render_ExemplosDeSimulador_TaxaContratadaEhPercentualNaoFracao()
    {
        var cut = RenderDocs();

        cut.Markup.Should().NotMatchRegex("\"?taxaContratada\"?\\s*[:=]\\s*0\\.\\d");
        cut.Markup.Should().Contain("taxaContratada\":10");

        foreach (var blocoId in new[] { "ep-post-simulador", "ep-post-simulador-cenarios" })
        {
            cut.Find($"#{blocoId} [data-testid=exemplo-csharp]").Click();
            cut.WaitForAssertion(() =>
                cut.Find($"#{blocoId} [data-testid=exemplo-codigo]").TextContent.Should().Contain("taxaContratada"));
            cut.Find($"#{blocoId} [data-testid=exemplo-codigo]").TextContent.Should().Contain("taxaContratada = 10m");
            cut.Markup.Should().NotMatchRegex("\"?taxaContratada\"?\\s*[:=]\\s*0\\.\\d");

            cut.Find($"#{blocoId} [data-testid=exemplo-js]").Click();
            cut.WaitForAssertion(() =>
                cut.Find($"#{blocoId} [data-testid=exemplo-codigo]").TextContent.Should().Contain("taxaContratada"));
            cut.Find($"#{blocoId} [data-testid=exemplo-codigo]").TextContent.Should().Contain("taxaContratada: 10");
            cut.Markup.Should().NotMatchRegex("\"?taxaContratada\"?\\s*[:=]\\s*0\\.\\d");
        }
    }

    [Fact]
    public void Click_AbaCSharpDeUmaInstancia_ForcandoRenderDaArvore_NaoAlteraAbaAtivaDeOutraInstancia()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<ExemploCodigo>(0);
            builder.AddAttribute(1, nameof(ExemploCodigo.CurlCodigo), "curl-A");
            builder.AddAttribute(2, nameof(ExemploCodigo.CSharpCodigo), "csharp-A");
            builder.AddAttribute(3, nameof(ExemploCodigo.JsCodigo), "js-A");
            builder.CloseComponent();

            builder.OpenComponent<ExemploCodigo>(4);
            builder.AddAttribute(5, nameof(ExemploCodigo.CurlCodigo), "curl-B");
            builder.AddAttribute(6, nameof(ExemploCodigo.CSharpCodigo), "csharp-B");
            builder.AddAttribute(7, nameof(ExemploCodigo.JsCodigo), "js-B");
            builder.CloseComponent();
        });

        var botoesCSharp = cut.FindAll("[data-testid=exemplo-csharp]");
        botoesCSharp[0].Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=exemplo-codigo]")[0].TextContent.Should().Contain("csharp-A");
        });

        var instancias = cut.FindComponents<ExemploCodigo>();
        instancias[0].Render();
        instancias[1].Render();

        var blocos = cut.FindAll("[data-testid=exemplo-codigo]");
        blocos[0].TextContent.Should().Contain("csharp-A");
        blocos[1].TextContent.Should().Contain("curl-B");
    }

    [Fact]
    public void Render_TabelaDeErros_NaoContem409NemQualquerCodigoQueNenhumEndpointDocumentadoEmite()
    {
        var cut = RenderDocs();

        var linhas = cut.FindAll("#erros table.params-table tbody tr");

        linhas.Select(l => l.QuerySelector("td")!.TextContent.Trim())
            .Should().BeEquivalentTo("400", "401", "404", "429");
    }

    [Fact]
    public void Render_DuasInstanciasDeExemploCodigo_TemAriaControlsDiferentesApontandoParaIdsExistentes()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<ExemploCodigo>(0);
            builder.AddAttribute(1, nameof(ExemploCodigo.CurlCodigo), "curl-A");
            builder.AddAttribute(2, nameof(ExemploCodigo.CSharpCodigo), "csharp-A");
            builder.AddAttribute(3, nameof(ExemploCodigo.JsCodigo), "js-A");
            builder.CloseComponent();

            builder.OpenComponent<ExemploCodigo>(4);
            builder.AddAttribute(5, nameof(ExemploCodigo.CurlCodigo), "curl-B");
            builder.AddAttribute(6, nameof(ExemploCodigo.CSharpCodigo), "csharp-B");
            builder.AddAttribute(7, nameof(ExemploCodigo.JsCodigo), "js-B");
            builder.CloseComponent();
        });

        var botoes = cut.FindAll("[data-testid=exemplo-curl]");
        var ariaControlsA = botoes[0].GetAttribute("aria-controls");
        var ariaControlsB = botoes[1].GetAttribute("aria-controls");

        ariaControlsA.Should().NotBeNullOrEmpty();
        ariaControlsB.Should().NotBeNullOrEmpty();
        ariaControlsA.Should().NotBe(ariaControlsB);

        cut.Find($"#{ariaControlsA}").Should().NotBeNull();
        cut.Find($"#{ariaControlsB}").Should().NotBeNull();

        var paineis = cut.FindAll("[data-testid=exemplo-codigo]");
        paineis[0].GetAttribute("role").Should().Be("tabpanel");
        paineis[1].GetAttribute("role").Should().Be("tabpanel");
    }
}
