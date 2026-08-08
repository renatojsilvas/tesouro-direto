namespace TesouroDireto.Infrastructure.RateLimiting;

public sealed record RateLimitingOptions(int PermitLimit, TimeSpan Window);
