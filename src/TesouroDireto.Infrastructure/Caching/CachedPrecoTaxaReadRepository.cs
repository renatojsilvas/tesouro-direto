using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedPrecoTaxaReadRepository(
    IPrecoTaxaReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    IConfiguration configuration) : IPrecoTaxaReadRepository
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    public Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> GetByTituloIdAsync(
        Guid tituloId,
        DateOnly? dataInicio,
        DateOnly? dataFim,
        CancellationToken cancellationToken)
    {
        return inner.GetByTituloIdAsync(tituloId, dataInicio, dataFim, cancellationToken);
    }

    public Task<Result<PrecoTaxaDto>> GetLatestByTituloIdAsync(
        Guid tituloId,
        CancellationToken cancellationToken)
    {
        var key = $"preco-atual:{tituloId}";

        return cache.GetOrCreateResultAsync(
            key,
            GetTtl(),
            invalidator.GetPrecosToken(),
            () => inner.GetLatestByTituloIdAsync(tituloId, cancellationToken));
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:PrecosTaxas") ?? DefaultTtl;
}
