using Prometheus;
using TesouroDireto.Application.Common.Interfaces;

namespace TesouroDireto.Infrastructure.Observability;

public sealed class BusinessMetrics : IBusinessMetrics
{
    private static readonly Gauge ImportLastSuccessTimestamp = Metrics.CreateGauge(
        "import_last_success_timestamp_seconds",
        "Timestamp (unix, UTC) da última importação de CSV bem-sucedida.");

    private static readonly Counter ImportRunsTotal = Metrics.CreateCounter(
        "import_runs_total",
        "Total de execuções de importação de CSV, por desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    private static readonly Counter SimulationsTotal = Metrics.CreateCounter(
        "simulations_total",
        "Total de simulações executadas, por indexador e desfecho (success|failure).",
        new CounterConfiguration
        {
            LabelNames = ["indexador", "outcome"]
        });

    private static readonly Counter SimulationFailuresTotal = Metrics.CreateCounter(
        "simulation_failures_total",
        "Total de falhas de simulação, por motivo (Error.Code).",
        new CounterConfiguration
        {
            LabelNames = ["reason"]
        });

    private static readonly Counter ImportPricesProcessedTotal = Metrics.CreateCounter(
        "import_prices_processed_total",
        "Total de preços/taxas processados em importações de CSV, por tipo (inserted|skipped|error).",
        new CounterConfiguration
        {
            LabelNames = ["kind"]
        });

    private static readonly Counter TributosConfigChangesTotal = Metrics.CreateCounter(
        "tributos_config_changes_total",
        "Total de alterações de configuração de tributos, por operação (create|update).",
        new CounterConfiguration
        {
            LabelNames = ["op"]
        });

    public void ImportSucceeded() => ImportLastSuccessTimestamp.SetToCurrentTimeUtc();

    public void RecordImportRun(string outcome) => ImportRunsTotal.WithLabels(outcome).Inc();

    public void RecordSimulation(string indexador, string outcome) =>
        SimulationsTotal.WithLabels(indexador, outcome).Inc();

    public void RecordSimulationFailure(string reason) =>
        SimulationFailuresTotal.WithLabels(reason).Inc();

    public void RecordPricesProcessed(string kind, long count) =>
        ImportPricesProcessedTotal.WithLabels(kind).Inc(count);

    public void RecordTributoConfigChange(string op) =>
        TributosConfigChangesTotal.WithLabels(op).Inc();
}
