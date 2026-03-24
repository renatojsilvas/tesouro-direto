using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedTributoReadRepository(
    ITributoReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator) : ITributoReadRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyCollection<Tributo>>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string key = "tributos:all";

        if (cache.TryGetValue(key, out IReadOnlyCollection<Tributo>? cached))
        {
            return Result<IReadOnlyCollection<Tributo>>.Success(cached!);
        }

        var result = await inner.GetAllAsync(cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetTributosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }

    public Task<Result<Tributo>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return inner.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result<IReadOnlyCollection<Tributo>>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken)
    {
        const string key = "tributos:ativos";

        if (cache.TryGetValue(key, out IReadOnlyCollection<Tributo>? cached))
        {
            return Result<IReadOnlyCollection<Tributo>>.Success(cached!);
        }

        var result = await inner.GetAtivosOrdenadosAsync(cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetTributosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }
}
