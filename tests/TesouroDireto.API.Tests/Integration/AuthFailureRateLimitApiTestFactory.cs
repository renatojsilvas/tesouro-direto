namespace TesouroDireto.API.Tests.Integration;

public sealed class AuthFailureRateLimitApiTestFactory : ApiTestFactory
{
    public AuthFailureRateLimitApiTestFactory()
    {
        ConfigOverrides = new Dictionary<string, string?>
        {
            ["RateLimiting:AuthFailure:PermitLimit"] = "3",
            ["RateLimiting:AuthFailure:WindowSeconds"] = "60",
        };
    }
}
