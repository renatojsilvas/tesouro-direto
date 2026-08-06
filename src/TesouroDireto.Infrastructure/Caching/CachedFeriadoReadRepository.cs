using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedFeriadoReadRepository(
    IFeriadoReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    IConfiguration configuration) : IFeriadoReadRepository
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    public Task<Result<IReadOnlyCollection<DateOnly>>> GetAllDatasAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateResultAsync(
            "feriados:datas",
            GetTtl(),
            invalidator.GetFeriadosToken(),
            () => inner.GetAllDatasAsync(cancellationToken));

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:Feriados") ?? DefaultTtl;
}
