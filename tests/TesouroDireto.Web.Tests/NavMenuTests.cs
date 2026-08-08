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

    [Fact]
    public void NavMenu_QuandoAnonimo_NaoMostraLinksDeContaNemAdmin()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.AddSingleton(new GoogleAuthAvailability(true));

        var cut = RenderComponent<NavMenu>();

        cut.FindAll("[data-testid=nav-minhas-api-keys]").Should().BeEmpty();
        cut.FindAll("[data-testid=nav-admin]").Should().BeEmpty();
    }

    [Fact]
    public void NavMenu_QuandoLogadoNaoAdmin_MostraApiKeysMasNaoAdmin()
    {
        this.AddTestAuthorization().SetAuthorized("user@x");
        Services.AddSingleton(new GoogleAuthAvailability(true));

        var cut = RenderComponent<NavMenu>();

        cut.FindAll("[data-testid=nav-minhas-api-keys]").Should().HaveCount(1);
        cut.FindAll("[data-testid=nav-admin]").Should().BeEmpty();
    }

    [Fact]
    public void NavMenu_QuandoLogadoAdmin_MostraApiKeysEAdmin()
    {
        this.AddTestAuthorization().SetAuthorized("admin@x").SetRoles("Admin");
        Services.AddSingleton(new GoogleAuthAvailability(true));

        var cut = RenderComponent<NavMenu>();

        cut.FindAll("[data-testid=nav-minhas-api-keys]").Should().HaveCount(1);
        cut.FindAll("[data-testid=nav-admin]").Should().HaveCount(1);
    }
}
