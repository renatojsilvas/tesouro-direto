using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TesouroDireto.Web.Services;

public sealed record ApiError(string Code, string Detail);

public sealed record ApiResult<T>(bool IsSuccess, T? Data, ApiError? Error, int StatusCode, string? RawBody);

public sealed record PageLinks(string? First, string? Prev, string? Next, string? Last);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, PageLinks Links);

public sealed class TesouroApiClient(HttpClient httpClient, IConditionalGetStore etagStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string relativeUri)
    {
        var (body, _) = await ConditionalGetAsync(relativeUri);
        return DeserializeBody<T>(body);
    }

    public async Task<PagedResponse<T>> GetPagedAsync<T>(string relativeUri)
    {
        var (body, headers) = await ConditionalGetAsync(relativeUri);
        var items = DeserializeBody<List<T>>(body) ?? [];
        var totalCount = headers.TryGetValue("X-Total-Count", out var totalCountValue) && int.TryParse(totalCountValue, out var parsedTotalCount)
            ? parsedTotalCount
            : 0;
        var links = ParseLinkHeader(headers.GetValueOrDefault("Link"));

        return new PagedResponse<T>(items, totalCount, links);
    }

    public async Task<ApiResult<T>> PostAsync<T>(string relativeUri, object body)
    {
        var response = await httpClient.PostAsJsonAsync(relativeUri, body);
        return await BuildResultAsync<T>(response);
    }

    public async Task<ApiResult<T>> PutAsync<T>(string relativeUri, object body)
    {
        var response = await httpClient.PutAsJsonAsync(relativeUri, body);
        return await BuildResultAsync<T>(response);
    }

    private async Task<(byte[] Body, IReadOnlyDictionary<string, string> Headers)> ConditionalGetAsync(string uri)
    {
        var hasCached = etagStore.TryGet(uri, out var cached);

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (hasCached && cached is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }

        var response = await httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (cached is null)
            {
                throw new InvalidOperationException($"Recebido 304 para '{uri}' sem entrada em cache correspondente.");
            }

            return (cached.Body, cached.Headers);
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync();
        var headers = new Dictionary<string, string>();

        if (response.Headers.TryGetValues("X-Total-Count", out var totalCountValues))
        {
            headers["X-Total-Count"] = string.Join(", ", totalCountValues);
        }

        if (response.Headers.TryGetValues("Link", out var linkValues))
        {
            headers["Link"] = string.Join(", ", linkValues);
        }

        if (response.Headers.ETag is not null)
        {
            etagStore.Set(uri, new ConditionalCacheEntry(response.Headers.ETag.ToString(), body, headers));
        }

        return (body, headers);
    }

    private static T? DeserializeBody<T>(byte[] body)
        => body.Length == 0
            ? default
            : JsonSerializer.Deserialize<T>(body, WebJsonOptions);

    private static PageLinks ParseLinkHeader(string? linkHeader)
    {
        if (string.IsNullOrWhiteSpace(linkHeader))
        {
            return new PageLinks(null, null, null, null);
        }

        string? first = null;
        string? prev = null;
        string? next = null;
        string? last = null;

        foreach (var linkValue in SplitLinkValues(linkHeader))
        {
            var trimmed = linkValue.Trim();
            var hrefStart = trimmed.IndexOf('<');
            var hrefEnd = trimmed.IndexOf('>');
            if (hrefStart < 0 || hrefEnd < 0 || hrefEnd <= hrefStart)
            {
                continue;
            }

            var href = trimmed[(hrefStart + 1)..hrefEnd];
            var relIndex = trimmed.IndexOf("rel=", hrefEnd, StringComparison.OrdinalIgnoreCase);
            if (relIndex < 0)
            {
                continue;
            }

            var rel = trimmed[(relIndex + 4)..].Trim().Trim('"');

            switch (rel)
            {
                case "first":
                    first = href;
                    break;
                case "prev":
                    prev = href;
                    break;
                case "next":
                    next = href;
                    break;
                case "last":
                    last = href;
                    break;
            }
        }

        return new PageLinks(first, prev, next, last);
    }

    private static IEnumerable<string> SplitLinkValues(string linkHeader)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < linkHeader.Length; i++)
        {
            var c = linkHeader[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(linkHeader[start..i]);
                start = i + 1;
            }
        }

        parts.Add(linkHeader[start..]);
        return parts;
    }

    private static async Task<ApiResult<T>> BuildResultAsync<T>(HttpResponseMessage response)
    {
        var rawBody = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            var data = string.IsNullOrWhiteSpace(rawBody)
                ? default
                : JsonSerializer.Deserialize<T>(rawBody, JsonOptions);

            return new ApiResult<T>(true, data, null, statusCode, rawBody);
        }

        ApiError? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ApiError>(rawBody, JsonOptions);
        }
        catch
        {
            error = null;
        }

        return new ApiResult<T>(false, default, error, statusCode, rawBody);
    }
}
