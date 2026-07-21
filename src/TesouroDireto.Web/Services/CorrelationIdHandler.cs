using System.Text.RegularExpressions;
using Serilog.Context;

namespace TesouroDireto.Web.Services;

public sealed partial class CorrelationIdHandler(ILogger<CorrelationIdHandler> logger) : DelegatingHandler
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = GetOrCreateCorrelationId(request);

        request.Headers.Remove(CorrelationIdHeader);
        request.Headers.Add(CorrelationIdHeader, correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            logger.LogInformation(
                "Chamando API {Method} {RequestUri}",
                request.Method,
                request.RequestUri);

            var response = await base.SendAsync(request, cancellationToken);

            logger.LogInformation(
                "API respondeu {StatusCode} para {Method} {RequestUri}",
                (int)response.StatusCode,
                request.Method,
                request.RequestUri);

            return response;
        }
    }

    private static string GetOrCreateCorrelationId(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues(CorrelationIdHeader, out var existing))
        {
            var existingValue = existing.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(existingValue) && ValidCorrelationIdPattern().IsMatch(existingValue))
            {
                return existingValue;
            }
        }

        return Guid.NewGuid().ToString();
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-]{1,64}$")]
    private static partial Regex ValidCorrelationIdPattern();
}
