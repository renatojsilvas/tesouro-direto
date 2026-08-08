using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Web.Components.Layout;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class NavMenuTests : TestContext
{
    [Fact]
    public void NavMenu_QuandoGoogleConfigurado_MostraLinkDeEntrar()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(true));

        var cut = RenderComponent<NavMenu>();

        cut.Markup.Should().Contain("Entrar com Google");
    }

    [Fact]
    public void NavMenu_QuandoGoogleNaoConfigurado_NaoMostraLinkDeEntrar()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(false));

        var cut = RenderComponent<NavMenu>();

        cut.Markup.Should().NotContain("Entrar com Google");
    }
}
