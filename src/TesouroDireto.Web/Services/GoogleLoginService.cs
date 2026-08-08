using TesouroDireto.Web.Contracts;

namespace TesouroDireto.Web.Services;

public sealed record GoogleLoginClaims(string Sub, string Email, string Nome, bool EmailVerified);

public sealed class GoogleLoginService(TesouroApiClient apiClient)
{
    public async Task<bool> ProcessLoginAsync(GoogleLoginClaims claims)
    {
        if (!claims.EmailVerified)
        {
            return false;
        }

        try
        {
            var request = new SyncUsuarioRequest(claims.Sub, claims.Email, claims.Nome, claims.EmailVerified);
            var result = await apiClient.SyncUsuarioAsync(request);
            return result.IsSuccess;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
