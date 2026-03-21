using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Application.Feriados;

public interface IFeriadoWriteRepository
{
    Task<Result> AddRangeAsync(IReadOnlyCollection<Feriado> feriados, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<DateOnly>>> GetExistingDatasAsync(CancellationToken cancellationToken);
}
