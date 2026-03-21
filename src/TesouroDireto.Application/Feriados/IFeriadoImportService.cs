namespace TesouroDireto.Application.Feriados;

public interface IFeriadoImportService
{
    IAsyncEnumerable<FeriadoRecord> GetFeriadosAsync(CancellationToken cancellationToken);
}
