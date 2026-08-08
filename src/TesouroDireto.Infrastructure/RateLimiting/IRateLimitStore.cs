using System.Threading.RateLimiting;

namespace TesouroDireto.Infrastructure.RateLimiting;

public interface IRateLimitStore
{
    RateLimitPartition<string> GetPartition(string? clienteId);
}
