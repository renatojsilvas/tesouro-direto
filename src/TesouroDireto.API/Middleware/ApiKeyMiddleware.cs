using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Context;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Infrastructure.Observability;
using TesouroDireto.Infrastructure.RateLimiting;

namespace TesouroDireto.API.Middleware;

public sealed class ApiKeyMiddleware
{
    public const string IdentityKindItemsKey = "ApiKeyIdentityKind";
    public const string ServiceIdentityKind = "service";
    public const string ClientIdentityKind = "client";

    private const string ApiKeyHeader = "X-Api-Key";
    private const string ServiceIdentity = "service";
    private const string UnknownIdentity = "unknown";
    private const string AuthorizedOutcome = "authorized";
    private const string UnauthorizedOutcome = "unauthorized";
    private const string ClienteIdProperty = "ClienteId";

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
            RecordUnauthorized(context);
            await WriteUnauthorizedAsync(context);
            return;
        }

        if (IsKeyValid(providedKey, _configuredKey))
        {
            await AuthorizeAsync(context, ServiceIdentity, ServiceIdentityKind);
            return;
        }

        var clienteId = await TryResolveClienteIdAsync(context, providedKey);

        if (clienteId is null)
        {
            _logger.LogWarning("Invalid API key attempt to {Path}", context.Request.Path);
            RecordUnauthorized(context);
            await WriteUnauthorizedAsync(context);
            return;
        }

        await AuthorizeAsync(context, clienteId, ClientIdentityKind);
    }

    private async Task AuthorizeAsync(HttpContext context, string clienteId, string identityKind)
    {
        var diagnosticContext = context.RequestServices.GetRequiredService<IDiagnosticContext>();
        var metrics = context.RequestServices.GetRequiredService<IApiKeyMetrics>();

        context.Items[IdentityKindItemsKey] = identityKind;

        if (identityKind == ClientIdentityKind)
        {
            context.Items[RateLimitIdentity.ClienteIdItemsKey] = clienteId;
        }

        diagnosticContext.Set(ClienteIdProperty, clienteId);
        metrics.RecordRequest(clienteId, AuthorizedOutcome);

        using (LogContext.PushProperty(ClienteIdProperty, clienteId))
        {
            await _next(context);
        }
    }

    private static void RecordUnauthorized(HttpContext context)
    {
        var metrics = context.RequestServices.GetRequiredService<IApiKeyMetrics>();
        metrics.RecordRequest(UnknownIdentity, UnauthorizedOutcome);
    }

    private static async Task<string?> TryResolveClienteIdAsync(HttpContext context, string providedKey)
    {
        var repository = context.RequestServices.GetRequiredService<IApiKeyReadRepository>();
        var hash = ApiKeyHash.FromRawKey(providedKey).Value;

        var result = await repository.GetActiveByHashAsync(hash, context.RequestAborted);

        return result.IsSuccess ? result.Value.Id.ToString() : null;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" }
        });
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
