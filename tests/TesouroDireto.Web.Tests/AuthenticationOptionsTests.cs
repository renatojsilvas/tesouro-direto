using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TesouroDireto.Web.Tests;

public class AuthenticationOptionsTests
{
    private sealed class WebTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }

    [Fact]
    public void CookieAuthenticationOptions_SeguemOAdrDeSessao()
    {
        using var factory = new WebTestFactory();
        using var scope = factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var options = optionsMonitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromHours(8));
        options.SlidingExpiration.Should().BeTrue();
        options.LoginPath.Value.Should().Be("/auth/login");
    }
}
