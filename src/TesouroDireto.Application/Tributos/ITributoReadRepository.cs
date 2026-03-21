using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public interface ITributoReadRepository
{
    Task<Result<IReadOnlyCollection<Tributo>>> GetAllAsync(CancellationToken cancellationToken);
    Task<Result<Tributo?>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<Tributo>>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken);
}
