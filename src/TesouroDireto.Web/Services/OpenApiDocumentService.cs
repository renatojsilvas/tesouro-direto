using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TesouroDireto.Web.Services;

public sealed class OpenApiDocumentService(
    TesouroApiClient api,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<OpenApiDocumentService> logger)
{
    private const string CacheKey = "openapi:documento-filtrado";
    private const string SwaggerRelativeUri = "swagger/v1/swagger.json";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<string?> GetDocumentAsync()
    {
        if (cache.TryGetValue(CacheKey, out string? cached))
        {
            return cached;
        }

        try
        {
            var raw = await api.GetAsync<JsonNode>(SwaggerRelativeUri);
            if (raw is null)
            {
                logger.LogWarning("Documento OpenAPI recebido da API veio vazio.");
                return null;
            }

            var publicBaseUrl = configuration["ApiSettings:PublicBaseUrl"] ?? string.Empty;
            var documento = Transform(raw.ToJsonString(), publicBaseUrl);

            cache.Set(CacheKey, documento, GetTtl());

            return documento;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Falha ao obter o documento OpenAPI da API.");
            return null;
        }
    }

    public static string Transform(string rawJson, string publicBaseUrl)
    {
        var node = JsonNode.Parse(rawJson)!.AsObject();

        if (node["paths"] is JsonObject paths)
        {
            var chavesRestritas = paths
                .Where(par => EhCaminhoRestrito(par.Key))
                .Select(par => par.Key)
                .ToList();

            foreach (var chave in chavesRestritas)
            {
                paths.Remove(chave);
            }
        }

        node["servers"] = new JsonArray(new JsonObject { ["url"] = publicBaseUrl });

        return node.ToJsonString();
    }

    private static bool EhCaminhoRestrito(string path) =>
        path.StartsWith("/v1/me", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/v1/admin", StringComparison.OrdinalIgnoreCase);

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:OpenApi") ?? DefaultTtl;
}
