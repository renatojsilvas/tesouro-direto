using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedTributoReadRepository(
    ITributoReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    IConfiguration configuration) : ITributoReadRepository
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public Task<Result<IReadOnlyCollection<Tributo>>> GetAllAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateResultAsync(
            "tributos:all",
            GetTtl(),
            invalidator.GetTributosToken(),
            () => inner.GetAllAsync(cancellationToken));

    public Task<Result<Tributo>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return inner.GetByIdAsync(id, cancellationToken);
    }

    public Task<Result<IReadOnlyCollection<Tributo>>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateResultAsync(
            "tributos:ativos",
            GetTtl(),
            invalidator.GetTributosToken(),
            () => inner.GetAtivosOrdenadosAsync(cancellationToken));

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:Tributos") ?? DefaultTtl;
}
