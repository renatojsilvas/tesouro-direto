using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Feriados;

public interface IDiasUteisService
{
    Task<Result<DiasUteisFeriados>> CalcularDiasUteisAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken);
}
