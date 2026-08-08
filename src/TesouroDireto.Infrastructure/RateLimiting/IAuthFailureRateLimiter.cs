namespace TesouroDireto.Infrastructure.RateLimiting;

public interface IAuthFailureRateLimiter
{
    bool RegisterFailure(string clientIp);
    TimeSpan Window { get; }
}
