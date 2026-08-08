using System.Threading.RateLimiting;

namespace TesouroDireto.Infrastructure.RateLimiting;

public sealed class RedisRateLimitStore : IRateLimitStore
{
    public RateLimitPartition<string> GetPartition(string? clienteId)
    {
        throw new NotImplementedException(
            "Rate limit distribuído (Redis) declarado como gancho e ainda não implementado; use RateLimiting:Store=InMemory.");
    }
}
