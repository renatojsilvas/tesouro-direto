using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Infrastructure.Http;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedContentVersionProvider(
    IContentVersionProvider inner,
    IMemoryCache cache,
    IConfiguration configuration) : IContentVersionProvider
{
    private const string CacheKey = "content-version";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var version = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GetTtl();
            return await inner.GetVersionAsync(cancellationToken);
        });

        return version ?? await inner.GetVersionAsync(cancellationToken);
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:ContentVersion") ?? DefaultTtl;
}
