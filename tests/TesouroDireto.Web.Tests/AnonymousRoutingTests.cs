using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class AnonymousRoutingTests : TestContext
{
    [Fact]
    public void Home_QuandoUsuarioNaoAutenticado_RenderizaSemExigirLogin()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(true));

        var cut = RenderComponent<Routes>();

        cut.Markup.Should().Contain("Tesouro Direto");
        cut.Markup.Should().Contain("Entrar com Google");
        cut.Markup.Should().NotContain(">Sair<");
    }
}
