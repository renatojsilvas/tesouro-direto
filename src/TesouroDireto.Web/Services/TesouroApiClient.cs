using System.Net.Http.Json;
using System.Text.Json;

namespace TesouroDireto.Web.Services;

public sealed record ApiError(string Code, string Description);

public sealed record ApiResult<T>(bool IsSuccess, T? Data, ApiError? Error, int StatusCode, string? RawBody);

public sealed class TesouroApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<T?> GetAsync<T>(string relativeUri)
        => httpClient.GetFromJsonAsync<T>(relativeUri);

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
