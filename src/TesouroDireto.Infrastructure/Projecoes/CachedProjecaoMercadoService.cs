using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Projecoes;

public sealed class CachedProjecaoMercadoService(
    IProjecaoMercadoService inner,
    IMemoryCache cache,
    MemoryCacheInvalidator invalidator,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<CachedProjecaoMercadoService> logger) : IProjecaoMercadoService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DefaultMaxFallbackAge = TimeSpan.FromDays(7);

    public async Task<Result<ProjecaoMercado>> GetProjecaoAsync(Indexador indexador, CancellationToken cancellationToken)
    {
        var freshKey = FreshKey(indexador);

        if (cache.TryGetValue(freshKey, out ProjecaoMercado? fresh))
        {
            return Result<ProjecaoMercado>.Success(fresh!);
        }

        var result = await inner.GetProjecaoAsync(indexador, cancellationToken);

        if (result.IsSuccess)
        {
            var projecao = result.Value with
            {
                ObtidaEmUtc = timeProvider.GetUtcNow(),
                Origem = OrigemProjecao.Bcb
            };

            var lkgKey = LkgKey(indexador);

            cache.Set(freshKey, projecao,
                new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(GetTtl())
                    .AddExpirationToken(new CancellationChangeToken(invalidator.GetProjecoesToken()))
                    .SetSize(1));

            cache.Set(lkgKey, projecao,
                new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(GetMaxFallbackAge())
                    .AddExpirationToken(new CancellationChangeToken(invalidator.GetProjecoesToken()))
                    .SetSize(1));

            return Result<ProjecaoMercado>.Success(projecao);
        }

        if (result.Error.Code != ProjecaoErrors.HttpError.Code)
        {
            return result;
        }

        if (cache.TryGetValue(LkgKey(indexador), out ProjecaoMercado? lkg))
        {
            var idade = timeProvider.GetUtcNow() - lkg!.ObtidaEmUtc;

            logger.LogWarning(
                "Projeção servida do cache (fallback): BCB indisponível para {Indexador}. Erro={ErrorCode}, ObtidaEm={ObtidaEmUtc}, IdadeHoras={IdadeHoras}",
                indexador.Name, result.Error.Code, lkg.ObtidaEmUtc, idade.TotalHours);

            return Result<ProjecaoMercado>.Success(lkg with { Origem = OrigemProjecao.CacheFallback });
        }

        return result;
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("FocusBcb:CacheTtl") ?? DefaultTtl;

    private TimeSpan GetMaxFallbackAge() =>
        configuration.GetValue<TimeSpan?>("FocusBcb:MaxFallbackAge") ?? DefaultMaxFallbackAge;

    private static string FreshKey(Indexador indexador) => $"projecao:fresh:{indexador.Name}";

    private static string LkgKey(Indexador indexador) => $"projecao:lkg:{indexador.Name}";
}
