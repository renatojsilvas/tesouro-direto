using System.Threading.RateLimiting;

namespace TesouroDireto.Infrastructure.RateLimiting;

public sealed class InMemoryAuthFailureRateLimiter : IAuthFailureRateLimiter
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly AuthFailureRateLimitingOptions _options;

    public InMemoryAuthFailureRateLimiter(AuthFailureRateLimitingOptions options)
    {
        _options = options;
        _limiter = PartitionedRateLimiter.Create<string, string>(ip =>
            RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = options.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public TimeSpan Window => _options.Window;

    public bool RegisterFailure(string clientIp)
    {
        using var lease = _limiter.AttemptAcquire(clientIp, 1);
        return lease.IsAcquired;
    }
}
