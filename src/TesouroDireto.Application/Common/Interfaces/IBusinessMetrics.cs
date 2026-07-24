namespace TesouroDireto.Application.Common.Interfaces;

/// <summary>
/// Port (O8) para métricas de negócio expostas via Prometheus. A implementação
/// concreta vive em Infrastructure (prometheus-net) — a Application depende só
/// desta abstração, sem conhecer o mecanismo de exportação.
/// </summary>
public interface IBusinessMetrics
{
    /// <summary>Marca o instante (unix time) da última importação de CSV bem-sucedida.</summary>
    void ImportSucceeded();

    /// <summary>Registra o desfecho de uma execução de importação (outcome: success|failure).</summary>
    void RecordImportRun(string outcome);

    /// <summary>Registra o desfecho de uma simulação, por indexador (outcome: success|failure).</summary>
    void RecordSimulation(string indexador, string outcome);

    /// <summary>Registra o motivo (Error.Code) de uma falha de simulação.</summary>
    void RecordSimulationFailure(string reason);

    /// <summary>Registra a contagem de preços/taxas processados por uma importação, por tipo (kind: inserted|skipped|error).</summary>
    void RecordPricesProcessed(string kind, long count);

    /// <summary>Registra uma alteração de configuração de tributo (op: create|update).</summary>
    void RecordTributoConfigChange(string op);
}
