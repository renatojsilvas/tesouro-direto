using FluentAssertions;
using Prometheus;
using TesouroDireto.Infrastructure.Observability;

namespace TesouroDireto.Infrastructure.Tests.Observability;

public sealed class BusinessMetricsTests
{
    // As métricas de BusinessMetrics são static no default registry do prometheus-net
    // (estado global compartilhado entre testes/instâncias). Cada teste captura o valor
    // ANTES e DEPOIS e assere o DELTA, nunca o valor absoluto — evita colisão entre
    // testes que rodam na mesma série/label.
    private static readonly Gauge ImportLastSuccessTimestamp = Metrics.CreateGauge(
        "import_last_success_timestamp_seconds", "help");

    private static readonly Counter ImportRunsTotal = Metrics.CreateCounter(
        "import_runs_total", "help", new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Counter SimulationsTotal = Metrics.CreateCounter(
        "simulations_total", "help", new CounterConfiguration { LabelNames = ["indexador", "outcome"] });

    private static readonly Counter SimulationFailuresTotal = Metrics.CreateCounter(
        "simulation_failures_total", "help", new CounterConfiguration { LabelNames = ["reason"] });

    private static readonly Counter ImportPricesProcessedTotal = Metrics.CreateCounter(
        "import_prices_processed_total", "help", new CounterConfiguration { LabelNames = ["kind"] });

    private static readonly Counter TributosConfigChangesTotal = Metrics.CreateCounter(
        "tributos_config_changes_total", "help", new CounterConfiguration { LabelNames = ["op"] });

    private readonly BusinessMetrics _metrics = new();

    [Fact]
    public void ImportSucceeded_ShouldSetGaugeToCurrentUnixTime()
    {
        var beforeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _metrics.ImportSucceeded();

        var value = ImportLastSuccessTimestamp.Value;
        var afterUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        value.Should().BeGreaterThan(0);
        value.Should().BeInRange(beforeUnix - 1, afterUnix + 1);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public void RecordImportRun_ShouldIncrementCounterForOutcome(string outcome)
    {
        var before = ImportRunsTotal.WithLabels(outcome).Value;

        _metrics.RecordImportRun(outcome);

        var after = ImportRunsTotal.WithLabels(outcome).Value;
        (after - before).Should().Be(1);
    }

    [Theory]
    [InlineData("Selic", "success")]
    [InlineData("IPCA", "failure")]
    public void RecordSimulation_ShouldIncrementCounterForIndexadorAndOutcome(string indexador, string outcome)
    {
        var before = SimulationsTotal.WithLabels(indexador, outcome).Value;

        _metrics.RecordSimulation(indexador, outcome);

        var after = SimulationsTotal.WithLabels(indexador, outcome).Value;
        (after - before).Should().Be(1);
    }

    [Fact]
    public void RecordSimulationFailure_ShouldIncrementCounterForReason()
    {
        const string reason = "Projecao.HttpError";
        var before = SimulationFailuresTotal.WithLabels(reason).Value;

        _metrics.RecordSimulationFailure(reason);

        var after = SimulationFailuresTotal.WithLabels(reason).Value;
        (after - before).Should().Be(1);
    }

    [Theory]
    [InlineData("inserted", 3)]
    [InlineData("skipped", 5)]
    [InlineData("error", 2)]
    public void RecordPricesProcessed_ShouldIncrementCounterByCountForKind(string kind, long count)
    {
        var before = ImportPricesProcessedTotal.WithLabels(kind).Value;

        _metrics.RecordPricesProcessed(kind, count);

        var after = ImportPricesProcessedTotal.WithLabels(kind).Value;
        (after - before).Should().Be(count);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public void RecordTributoConfigChange_ShouldIncrementCounterForOp(string op)
    {
        var before = TributosConfigChangesTotal.WithLabels(op).Value;

        _metrics.RecordTributoConfigChange(op);

        var after = TributosConfigChangesTotal.WithLabels(op).Value;
        (after - before).Should().Be(1);
    }
}
