using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Feriados;

public interface IFeriadoReadRepository
{
    Task<Result<IReadOnlyCollection<DateOnly>>> GetAllDatasAsync(CancellationToken cancellationToken);
}
