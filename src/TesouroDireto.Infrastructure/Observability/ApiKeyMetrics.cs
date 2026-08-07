using Prometheus;

namespace TesouroDireto.Infrastructure.Observability;

public sealed class ApiKeyMetrics : IApiKeyMetrics
{
    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total",
        "Total de requisições autenticadas via API key, por cliente e desfecho (authorized|unauthorized).",
        new CounterConfiguration
        {
            LabelNames = ["cliente", "outcome"]
        });

    public void RecordRequest(string cliente, string outcome) =>
        ApiKeyRequestsTotal.WithLabels(cliente, outcome).Inc();
}
