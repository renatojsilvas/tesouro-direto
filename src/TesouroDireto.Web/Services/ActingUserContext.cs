using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TesouroDireto.Web.Services;

public interface IActingUserContext
{
    Task<string?> GetGoogleSubAsync();
}

public sealed class ActingUserContext(AuthenticationStateProvider authenticationStateProvider) : IActingUserContext
{
    public async Task<string?> GetGoogleSubAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authenticationState.User;

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    }
}
