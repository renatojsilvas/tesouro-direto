using System.Security.Cryptography;
using System.Text;

namespace TesouroDireto.API.Middleware;

public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly string _configuredKey;
    private readonly string[] _excludedPaths;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _configuredKey = configuration["ApiKey:Key"] ?? string.Empty;
        _excludedPaths = configuration.GetSection("ApiKey:ExcludedPaths").Get<string[]>() ?? [];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!TryGetValidApiKey(context, out var providedKey))
        {
            _logger.LogWarning("Request without API key to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!IsKeyValid(providedKey, _configuredKey))
        {
            _logger.LogWarning("Invalid API key attempt to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }

    private bool IsExcludedPath(PathString path)
    {
        return _excludedPaths.Any(excluded =>
            path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetValidApiKey(HttpContext context, out string apiKey)
    {
        apiKey = string.Empty;

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var headerValue))
        {
            return false;
        }

        var value = headerValue.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        apiKey = value;
        return true;
    }

    private static bool IsKeyValid(string providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(configuredKey))
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }
}
