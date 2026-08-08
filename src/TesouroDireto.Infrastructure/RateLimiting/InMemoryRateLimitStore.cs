using System.Threading.RateLimiting;

namespace TesouroDireto.Infrastructure.RateLimiting;

public sealed class InMemoryRateLimitStore(RateLimitingOptions options) : IRateLimitStore
{
    public RateLimitPartition<string> GetPartition(string? clienteId)
    {
        if (string.IsNullOrEmpty(clienteId))
        {
            return RateLimitPartition.GetNoLimiter<string>("__none__");
        }

        return RateLimitPartition.GetFixedWindowLimiter(clienteId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = options.Window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
