using System.Net;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TesouroDireto.Web.Tests;

[Collection(GoogleConfigCollection.Name)]
public class OAuthForwardedHeadersTests
{
    private const string ClientIdEnvVar = "Google__ClientId";
    private const string ClientSecretEnvVar = "Google__ClientSecret";

    private sealed class WebComGoogleFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }

    [Fact]
    public async Task AuthLogin_ComXForwardedProtoHttps_MontaRedirectUriComEsquemaHttps()
    {
        Environment.SetEnvironmentVariable(ClientIdEnvVar, "dummy-id");
        Environment.SetEnvironmentVariable(ClientSecretEnvVar, "dummy-secret");

        try
        {
            using var factory = new WebComGoogleFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Host = "dadosdotesourodireto.com.br";

            using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
            request.Headers.Add("X-Forwarded-Proto", "https");

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var location = ResolveLocation(response);
            location.Host.Should().Be("accounts.google.com");

            var query = HttpUtility.ParseQueryString(location.Query);
            var redirectUri = query["redirect_uri"];

            redirectUri.Should().Be("https://dadosdotesourodireto.com.br/signin-google");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientIdEnvVar, null);
            Environment.SetEnvironmentVariable(ClientSecretEnvVar, null);
        }
    }

    [Fact]
    public async Task AuthLogin_SemXForwardedProto_MontaRedirectUriComEsquemaHttp()
    {
        Environment.SetEnvironmentVariable(ClientIdEnvVar, "dummy-id");
        Environment.SetEnvironmentVariable(ClientSecretEnvVar, "dummy-secret");

        try
        {
            using var factory = new WebComGoogleFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Host = "dadosdotesourodireto.com.br";

            var response = await client.GetAsync("/auth/login");

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var location = ResolveLocation(response);
            location.Host.Should().Be("accounts.google.com");

            var query = HttpUtility.ParseQueryString(location.Query);
            var redirectUri = query["redirect_uri"];

            redirectUri.Should().Be("http://dadosdotesourodireto.com.br/signin-google");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientIdEnvVar, null);
            Environment.SetEnvironmentVariable(ClientSecretEnvVar, null);
        }
    }

    private static Uri ResolveLocation(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        location.Should().NotBeNull();

        return location!.IsAbsoluteUri
            ? location
            : new Uri(response.RequestMessage!.RequestUri!, location);
    }
}
