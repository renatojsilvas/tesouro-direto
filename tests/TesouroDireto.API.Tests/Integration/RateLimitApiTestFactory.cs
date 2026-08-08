namespace TesouroDireto.API.Tests.Integration;

public sealed class RateLimitApiTestFactory : ApiTestFactory
{
    public RateLimitApiTestFactory()
    {
        ConfigOverrides = new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = "3",
            ["RateLimiting:WindowSeconds"] = "60",
        };
    }
}
