using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public static class MemoryCacheResultExtensions
{
    public static async Task<Result<T>> GetOrCreateResultAsync<T>(
        this IMemoryCache cache,
        string key,
        TimeSpan ttl,
        CancellationToken invalidationToken,
        Func<Task<Result<T>>> factory)
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return Result<T>.Success(cached!);
        }

        var result = await factory();

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidationToken))
                .SetSize(1);

            cache.Set(key, result.Value, options);
        }

        return result;
    }
}
