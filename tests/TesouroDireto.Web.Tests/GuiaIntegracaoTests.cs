using Bunit;
using FluentAssertions;
using TesouroDireto.Web.Components.Shared;

namespace TesouroDireto.Web.Tests;

public class GuiaIntegracaoTests : TestContext
{
    [Fact]
    public void Render_ExemplosDePaginacao_UsamPageEPageSizeENaoPaginaETamanho()
    {
        var cut = RenderComponent<GuiaIntegracao>();

        var markup = cut.Markup;

        markup.Should().Contain("pageSize=");
        markup.Should().NotContain("pagina=");
        markup.Should().NotContain("tamanho=");
    }

    [Fact]
    public void Render_ComBaseUrlPersonalizada_UsaBaseUrlNosExemplos()
    {
        var cut = RenderComponent<GuiaIntegracao>(parameters => parameters
            .Add(p => p.BaseUrl, "https://api.exemplo.teste/v1"));

        cut.Markup.Should().Contain("https://api.exemplo.teste/v1");
    }

    [Fact]
    public void Render_SemParametro_ExibeTresPassosDoInicioRapido()
    {
        var cut = RenderComponent<GuiaIntegracao>();

        var markup = cut.Markup;

        markup.Should().Contain("Pegue sua chave");
        markup.Should().Contain("Faça a primeira requisição");
        markup.Should().Contain("Siga o caminho quente");
    }

    [Fact]
    public void Render_LinkParaCredenciais_ApontaParaDesenvolvedores()
    {
        var cut = RenderComponent<GuiaIntegracao>();

        cut.Find("a[href='/desenvolvedores']").Should().NotBeNull();
    }
}
