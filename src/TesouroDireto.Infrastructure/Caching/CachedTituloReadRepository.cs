using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedTituloReadRepository(
    ITituloReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator) : ITituloReadRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(
        string? indexador,
        bool? vencido,
        CancellationToken cancellationToken)
    {
        var key = $"titulos:{indexador ?? "all"}:{vencido?.ToString() ?? "all"}";

        if (cache.TryGetValue(key, out IReadOnlyCollection<TituloDto>? cached))
        {
            return Result<IReadOnlyCollection<TituloDto>>.Success(cached!);
        }

        var result = await inner.GetFilteredAsync(indexador, vencido, cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetTitulosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }

    public async Task<Result<TituloDto>> GetByNomeAsync(string nome, CancellationToken cancellationToken)
    {
        var key = $"titulo-nome:{nome.Trim().ToUpperInvariant()}";

        if (cache.TryGetValue(key, out TituloDto? cached))
        {
            return Result<TituloDto>.Success(cached!);
        }

        var result = await inner.GetByNomeAsync(nome, cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetTitulosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }
}
