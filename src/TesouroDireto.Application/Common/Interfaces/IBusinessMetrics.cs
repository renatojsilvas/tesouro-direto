namespace TesouroDireto.Application.Common.Interfaces;

public interface IBusinessMetrics
{
    void ImportSucceeded();

    void RecordImportRun(string outcome);

    void RecordSimulation(string indexador, string outcome);

    void RecordSimulationFailure(string reason);

    void RecordPricesProcessed(string kind, long count);

    void RecordTributoConfigChange(string op);
}
