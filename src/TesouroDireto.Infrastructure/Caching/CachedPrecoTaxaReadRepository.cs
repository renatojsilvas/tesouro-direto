using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedPrecoTaxaReadRepository(
    IPrecoTaxaReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator) : IPrecoTaxaReadRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public Task<Result<bool>> TituloExistsAsync(Guid tituloId, CancellationToken cancellationToken)
    {
        return inner.TituloExistsAsync(tituloId, cancellationToken);
    }

    public Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> GetByTituloIdAsync(
        Guid tituloId,
        DateOnly? dataInicio,
        DateOnly? dataFim,
        CancellationToken cancellationToken)
    {
        return inner.GetByTituloIdAsync(tituloId, dataInicio, dataFim, cancellationToken);
    }

    public async Task<Result<PrecoTaxaDto>> GetLatestByTituloIdAsync(
        Guid tituloId,
        CancellationToken cancellationToken)
    {
        var key = $"preco-atual:{tituloId}";

        if (cache.TryGetValue(key, out PrecoTaxaDto? cached))
        {
            return Result<PrecoTaxaDto>.Success(cached!);
        }

        var result = await inner.GetLatestByTituloIdAsync(tituloId, cancellationToken);

        if (result.IsSuccess)
        {
            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(Ttl)
                .AddExpirationToken(new CancellationChangeToken(invalidator.GetPrecosToken()));

            cache.Set(key, result.Value, options);
        }

        return result;
    }
}
