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
    public void Click_AbaCSharpDeUmaInstancia_NaoAlteraAbaAtivaDeOutraInstancia()
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

        cut.FindAll("[data-testid=exemplo-curl]")[0].TextContent.Should().NotBeNull();
        var botoesCSharp = cut.FindAll("[data-testid=exemplo-csharp]");
        botoesCSharp[0].Click();

        cut.WaitForAssertion(() =>
        {
            var blocos = cut.FindAll("[data-testid=exemplo-codigo]");
            blocos[0].TextContent.Should().Contain("csharp-A");
            blocos[1].TextContent.Should().Contain("curl-B");
        });
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
}
