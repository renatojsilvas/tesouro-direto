using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TesouroDireto.Web.Tests;

[Collection(GoogleConfigCollection.Name)]
public class AnonymousAccessWithoutGoogleTests
{
    private sealed class WebSemGoogleFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Google:ClientId"] = "",
                    ["Google:ClientSecret"] = ""
                });
            });
        }
    }

    [Fact]
    public async Task Home_QuandoGoogleNaoConfigurado_NaoDevolve500()
    {
        using var factory = new WebSemGoogleFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [Fact]
    public async Task PaginaEstatica_QuandoGoogleNaoConfigurado_NaoDevolve500()
    {
        using var factory = new WebSemGoogleFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/about");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [Fact]
    public async Task AuthLogin_QuandoGoogleNaoConfigurado_RedirecionaSemChallenge()
    {
        using var factory = new WebSemGoogleFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/auth/login");

        ((int)response.StatusCode).Should().BeLessThan(500);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public void GoogleAuthAvailability_QuandoConfigVazia_IndicaNaoConfigurado()
    {
        using var factory = new WebSemGoogleFactory();
        using var scope = factory.Services.CreateScope();
        var availability = scope.ServiceProvider.GetRequiredService<Services.GoogleAuthAvailability>();

        availability.Configurado.Should().BeFalse();
    }
}
