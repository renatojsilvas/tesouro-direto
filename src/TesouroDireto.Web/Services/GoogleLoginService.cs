using TesouroDireto.Web.Contracts;

namespace TesouroDireto.Web.Services;

public sealed record GoogleLoginClaims(string Sub, string Email, string Nome, bool EmailVerified);

public sealed record GoogleLoginOutcome(bool Ok, string? Papel);

public sealed class GoogleLoginService(TesouroApiClient apiClient)
{
    public async Task<GoogleLoginOutcome> ProcessLoginAsync(GoogleLoginClaims claims)
    {
        if (!claims.EmailVerified)
        {
            return new GoogleLoginOutcome(false, null);
        }

        try
        {
            var request = new SyncUsuarioRequest(claims.Sub, claims.Email, claims.Nome, claims.EmailVerified);
            var result = await apiClient.SyncUsuarioAsync(request);
            return result.IsSuccess
                ? new GoogleLoginOutcome(true, result.Data?.Papel)
                : new GoogleLoginOutcome(false, null);
        }
        catch (HttpRequestException)
        {
            return new GoogleLoginOutcome(false, null);
        }
        catch (TaskCanceledException)
        {
            return new GoogleLoginOutcome(false, null);
        }
    }
}
