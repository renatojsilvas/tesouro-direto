namespace TesouroDireto.Infrastructure.Observability;

public interface IApiKeyMetrics
{
    void RecordRequest(string cliente, string outcome);
}
