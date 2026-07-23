using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Projecoes;

/// <summary>
/// Decorator de cache para <see cref="IProjecaoMercadoService"/>: desacopla o Simulador
/// da disponibilidade do BCB Focus. Mantém, por indexador, uma entrada "fresh" (TTL curto,
/// default 6h — evita bater no BCB a cada simulação) e uma entrada "lkg" (last known good,
/// TTL longo, default 7 dias — usada como fallback quando o BCB está fora do ar).
///
/// O fallback nunca é silencioso: a projeção devolvida sinaliza
/// <see cref="OrigemProjecao.CacheFallback"/> e um warning estruturado é logado toda vez
/// que é usado. A idade máxima do fallback é a própria expiração da entrada "lkg" no
/// cache — não há comparação de data feita à mão para decidir se a projeção está velha
/// demais; entrada ausente já significa "velha demais".
/// </summary>
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
            // Recarimba com o TimeProvider do decorator (real em produção, controlável
            // em teste) — é este valor, e não o que o inner devolveu, que efetivamente
            // chega a quem consome IProjecaoMercadoService.
            var projecao = result.Value with
            {
                ObtidaEmUtc = timeProvider.GetUtcNow(),
                Origem = OrigemProjecao.Bcb
            };

            var lkgKey = LkgKey(indexador);

            cache.Set(freshKey, projecao,
                new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(GetTtl())
                    .AddExpirationToken(new CancellationChangeToken(invalidator.GetProjecoesToken())));

            cache.Set(lkgKey, projecao,
                new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(GetMaxFallbackAge())
                    .AddExpirationToken(new CancellationChangeToken(invalidator.GetProjecoesToken())));

            return Result<ProjecaoMercado>.Success(projecao);
        }

        if (result.Error.Code != ProjecaoErrors.HttpError.Code)
        {
            // Falhas que não são de indisponibilidade do BCB (indexador não suportado,
            // URL mal configurada, resposta sem dados) não são cobertas por fallback —
            // usar uma projeção velha para mascarar um erro de configuração/dados seria
            // enganoso. Propaga sem consultar nem gravar o cache.
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
