using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedApiKeyReadRepository(
    IApiKeyReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    IConfiguration configuration) : IApiKeyReadRepository
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public Task<Result<ApiKeyDto>> GetByHashAsync(string hash, CancellationToken cancellationToken) =>
        inner.GetByHashAsync(hash, cancellationToken);

    public Task<Result<ApiKeyDto>> GetActiveByHashAsync(string hash, CancellationToken cancellationToken)
    {
        var key = $"apikey:{hash}";

        return cache.GetOrCreateResultAsync(
            key,
            GetTtl(),
            invalidator.GetApiKeysToken(),
            () => inner.GetActiveByHashAsync(hash, cancellationToken));
    }

    public Task<Result<IReadOnlyCollection<ApiKeyDto>>> ListByDonoAsync(Guid donoUsuarioId, CancellationToken cancellationToken) =>
        inner.ListByDonoAsync(donoUsuarioId, cancellationToken);

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:ApiKeys") ?? DefaultTtl;
}
