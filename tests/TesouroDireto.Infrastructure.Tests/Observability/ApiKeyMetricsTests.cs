using FluentAssertions;
using Prometheus;
using TesouroDireto.Infrastructure.Observability;

namespace TesouroDireto.Infrastructure.Tests.Observability;

public sealed class ApiKeyMetricsTests
{
    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total", "help", new CounterConfiguration { LabelNames = ["cliente", "outcome"] });

    private readonly ApiKeyMetrics _metrics = new();

    [Theory]
    [InlineData("service", "authorized")]
    [InlineData("unknown", "unauthorized")]
    public void RecordRequest_ShouldIncrementCounterForClienteAndOutcome(string cliente, string outcome)
    {
        var before = ApiKeyRequestsTotal.WithLabels(cliente, outcome).Value;

        _metrics.RecordRequest(cliente, outcome);

        var after = ApiKeyRequestsTotal.WithLabels(cliente, outcome).Value;
        (after - before).Should().Be(1);
    }

    [Fact]
    public void RecordRequest_WithClientApiKeyId_ShouldIncrementCounterForThatCliente()
    {
        var apiKeyId = Guid.NewGuid().ToString();
        var before = ApiKeyRequestsTotal.WithLabels(apiKeyId, "authorized").Value;

        _metrics.RecordRequest(apiKeyId, "authorized");

        var after = ApiKeyRequestsTotal.WithLabels(apiKeyId, "authorized").Value;
        (after - before).Should().Be(1);
    }
}
