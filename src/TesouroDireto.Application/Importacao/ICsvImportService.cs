namespace TesouroDireto.Application.Importacao;

public interface ICsvImportService
{
    IAsyncEnumerable<CsvRecordLine> GetRecordsAsync(CancellationToken cancellationToken);
}
