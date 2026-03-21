using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Importacao;

public interface ICsvImportService
{
    IAsyncEnumerable<Result<CsvRecord>> GetRecordsAsync(CancellationToken cancellationToken);
}
