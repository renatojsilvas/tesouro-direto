namespace TesouroDireto.Infrastructure.RateLimiting;

public sealed record AuthFailureRateLimitingOptions(int PermitLimit, TimeSpan Window);
