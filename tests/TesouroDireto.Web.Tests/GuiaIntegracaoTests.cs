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
}
