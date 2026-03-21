using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public interface ITituloReadRepository
{
    Task<IReadOnlyCollection<Titulo>> GetAllAsync(CancellationToken cancellationToken);
    Task<Titulo?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
