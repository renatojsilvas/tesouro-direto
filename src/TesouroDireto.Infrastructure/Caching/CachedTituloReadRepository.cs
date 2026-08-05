using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CachedTituloReadRepository(
    ITituloReadRepository inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    IConfiguration configuration) : ITituloReadRepository
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(
        string? indexador,
        bool? vencido,
        CancellationToken cancellationToken)
    {
        var key = $"titulos:{indexador ?? "all"}:{vencido?.ToString() ?? "all"}";

        return cache.GetOrCreateResultAsync(
            key,
            GetTtl(),
            invalidator.GetTitulosToken(),
            () => inner.GetFilteredAsync(indexador, vencido, cancellationToken));
    }

    public Task<Result<Guid>> GetIdByNomeAsync(string nome, CancellationToken cancellationToken)
    {
        var key = $"titulo-nome:{nome.Trim().ToUpperInvariant()}";

        return cache.GetOrCreateResultAsync(
            key,
            GetTtl(),
            invalidator.GetTitulosToken(),
            () => inner.GetIdByNomeAsync(nome, cancellationToken));
    }

    public Task<Result<Guid>> GetIdByCodigoAsync(string codigo, CancellationToken cancellationToken)
    {
        var key = $"titulo-codigo:{codigo}";

        return cache.GetOrCreateResultAsync(
            key,
            GetTtl(),
            invalidator.GetTitulosToken(),
            () => inner.GetIdByCodigoAsync(codigo, cancellationToken));
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:Titulos") ?? DefaultTtl;
}
