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

            if (node["components"] is JsonObject components && components["schemas"] is JsonObject schemas)
            {
                PodarSchemasNaoAlcancaveis(paths, schemas);
            }

            RemoverPrefixoV1(paths);
        }

        node["servers"] = new JsonArray(new JsonObject { ["url"] = publicBaseUrl });

        return node.ToJsonString();
    }

    private static bool EhCaminhoRestrito(string path) =>
        EhCaminhoOuSubcaminho(path, "/v1/me") || EhCaminhoOuSubcaminho(path, "/v1/admin");

    private static bool EhCaminhoOuSubcaminho(string path, string prefixo) =>
        path.Equals(prefixo, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefixo + "/", StringComparison.OrdinalIgnoreCase);

    private static void RemoverPrefixoV1(JsonObject paths)
    {
        var chaves = paths.Select(par => par.Key).ToList();

        foreach (var chave in chaves)
        {
            if (!chave.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valor = paths[chave];
            paths.Remove(chave);
            paths[chave["/v1".Length..]] = valor;
        }
    }

    private static void PodarSchemasNaoAlcancaveis(JsonObject paths, JsonObject schemas)
    {
        var alcancaveis = new HashSet<string>(StringComparer.Ordinal);
        var pendentes = new Queue<string>(ColetarNomesDeSchemasReferenciados(paths));

        while (pendentes.Count > 0)
        {
            var nome = pendentes.Dequeue();

            if (!alcancaveis.Add(nome))
            {
                continue;
            }

            if (schemas.TryGetPropertyValue(nome, out var schemaNode) && schemaNode is not null)
            {
                foreach (var nomeReferenciado in ColetarNomesDeSchemasReferenciados(schemaNode))
                {
                    pendentes.Enqueue(nomeReferenciado);
                }
            }
        }

        var chaves = schemas.Select(par => par.Key).ToList();

        foreach (var chave in chaves)
        {
            if (!alcancaveis.Contains(chave))
            {
                schemas.Remove(chave);
            }
        }
    }

    private static IEnumerable<string> ColetarNomesDeSchemasReferenciados(JsonNode? node)
    {
        const string prefixoRef = "#/components/schemas/";

        if (node is JsonObject obj)
        {
            foreach (var (chave, valor) in obj)
            {
                if (chave == "$ref" && valor is JsonValue refValue &&
                    refValue.TryGetValue(out string? refString) &&
                    refString is not null && refString.StartsWith(prefixoRef, StringComparison.Ordinal))
                {
                    yield return refString[(refString.LastIndexOf('/') + 1)..];
                    continue;
                }

                foreach (var nome in ColetarNomesDeSchemasReferenciados(valor))
                {
                    yield return nome;
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                foreach (var nome in ColetarNomesDeSchemasReferenciados(item))
                {
                    yield return nome;
                }
            }
        }
    }

    private TimeSpan GetTtl() =>
        configuration.GetValue<TimeSpan?>("Caching:OpenApi") ?? DefaultTtl;
}
