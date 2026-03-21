using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public interface ITributoReadRepository
{
    Task<IReadOnlyCollection<Tributo>> GetAllAsync(CancellationToken cancellationToken);
    Task<Tributo?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Tributo>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken);
}
