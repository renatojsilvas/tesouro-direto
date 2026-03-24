using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedFeriadoReadRepository(
    IFeriadoReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator) : IFeriadoReadRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public async Task<Result<IReadOnlyCollection<DateOnly>>> GetAllDatasAsync(CancellationToken cancellationToken)
    {
        const string key = "feriados:datas";

        if (cache.TryGetValue(key, out IReadOnlyCollection<DateOnly>? cached))
        {
            return Result<IReadOnlyCollection<DateOnly>>.Success(cached!);
        }

        var result = await inner.GetAllDatasAsync(cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetFeriadosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }
}
