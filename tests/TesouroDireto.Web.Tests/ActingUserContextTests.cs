using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class ActingUserContextTests
{
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    [Fact]
    public async Task GetGoogleSubAsync_QuandoAutenticado_DevolveClaimNameIdentifier()
    {
        var identidade = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "abc123")], "TestAuth");
        var provider = new FakeAuthenticationStateProvider(new ClaimsPrincipal(identidade));
        var actingUserContext = new ActingUserContext(provider);

        var sub = await actingUserContext.GetGoogleSubAsync();

        sub.Should().Be("abc123");
    }

    [Fact]
    public async Task GetGoogleSubAsync_QuandoAnonimo_DevolveNull()
    {
        var provider = new FakeAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity()));
        var actingUserContext = new ActingUserContext(provider);

        var sub = await actingUserContext.GetGoogleSubAsync();

        sub.Should().BeNull();
    }
}
