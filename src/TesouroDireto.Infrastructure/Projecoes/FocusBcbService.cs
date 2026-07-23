using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Projecoes;

public sealed class FocusBcbService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<FocusBcbService> logger) : IProjecaoMercadoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<ProjecaoMercado>> GetProjecaoAsync(
        Indexador indexador, CancellationToken cancellationToken)
    {
        if (indexador == Indexador.Prefixado)
        {
            return ProjecaoErrors.IndexadorNaoSuportado;
        }

        var baseUrl = configuration["FocusBcb:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return ProjecaoErrors.UrlNotConfigured;
        }

        var requestUrl = BuildRequestUrl(baseUrl, indexador);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUrl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelamento pedido por quem chamou é legítimo — não é uma falha do BCB,
            // então não deve virar HttpError nem disparar fallback de cache.
            throw;
        }
        catch (Exception ex)
        {
            // Catch amplo de propósito: cobre não só HttpRequestException, mas também
            // o que a resiliência (Polly, tarefa 13) pode lançar em cima do mesmo
            // GetAsync — BrokenCircuitException (circuito aberto) e
            // TimeoutRejectedException/TaskCanceledException (timeout de política, não
            // cancelamento do chamador). Sem isso, essas exceções escapariam para o
            // handler global (500 sem fallback), justo o caso que este decorator existe
            // para cobrir.
            logger.LogError(ex, "Failed to fetch projection from BCB Focus API for {Indexador}", indexador.Name);
            return ProjecaoErrors.HttpError;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "BCB Focus API returned {StatusCode} for {Indexador}",
                response.StatusCode, indexador.Name);
            response.Dispose();
            return ProjecaoErrors.HttpError;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var odata = await JsonSerializer.DeserializeAsync<ODataResponse>(stream, JsonOptions, cancellationToken);
        response.Dispose();

        if (odata?.Value is not { Count: > 0 })
        {
            return ProjecaoErrors.NotFound;
        }

        var entry = odata.Value[0];

        // ObtidaEmUtc/Origem aqui são só o valor inicial "cru" do fetch. O
        // CachedProjecaoMercadoService (decorator registrado como IProjecaoMercadoService
        // no DI) sempre recarimba ObtidaEmUtc com o próprio TimeProvider antes de
        // cachear/devolver — então este UtcNow só é o valor final para quem consome o
        // FocusBcbService diretamente (hoje, só os testes unitários dele).
        return new ProjecaoMercado(
            entry.Indicador,
            DateOnly.Parse(entry.Data),
            entry.Media,
            entry.Mediana,
            DateTimeOffset.UtcNow,
            OrigemProjecao.Bcb);
    }

    private static string BuildRequestUrl(string baseUrl, Indexador indexador)
    {
        var normalizedBase = baseUrl.TrimEnd('/');

        if (indexador == Indexador.Selic)
        {
            return $"{normalizedBase}/ExpectativasMercadoSelic?$top=1&$orderby=Data%20desc&$format=json";
        }

        var indicador = indexador == Indexador.IGPM ? "IGP-M" : indexador.Name;
        return $"{normalizedBase}/ExpectativasMercadoInflacao12Meses?$top=1&$filter=Indicador%20eq%20'{indicador}'&$orderby=Data%20desc&$format=json";
    }

    private sealed class ODataResponse
    {
        public List<ODataEntry> Value { get; set; } = [];
    }

    private sealed class ODataEntry
    {
        public string Indicador { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public decimal Media { get; set; }
        public decimal Mediana { get; set; }
    }
}
