using Microsoft.AspNetCore.Http;

namespace TesouroDireto.API.Http;

public sealed class ForwardedPrefixMiddleware
{
    private const string ForwardedPrefixHeader = "X-Forwarded-Prefix";

    private readonly RequestDelegate _next;
    private readonly string[] _allowedPrefixes;

    public ForwardedPrefixMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _allowedPrefixes = configuration.GetSection("ForwardedPrefix:Allowed").Get<string[]>() ?? [];
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (TryGetAllowedPrefix(context, out var prefix))
        {
            context.Request.PathBase = new PathString(prefix);
        }

        return _next(context);
    }

    private bool TryGetAllowedPrefix(HttpContext context, out string prefix)
    {
        prefix = string.Empty;

        if (_allowedPrefixes.Length == 0)
        {
            return false;
        }

        if (!context.Request.Headers.TryGetValue(ForwardedPrefixHeader, out var values) || values.Count != 1)
        {
            return false;
        }

        var candidate = values[0];

        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (!_allowedPrefixes.Contains(candidate, StringComparer.Ordinal))
        {
            return false;
        }

        prefix = candidate;
        return true;
    }
}
