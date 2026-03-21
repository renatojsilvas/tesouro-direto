using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public interface ITituloReadRepository
{
    Task<IReadOnlyCollection<Titulo>> GetAllAsync(CancellationToken cancellationToken);
    Task<Titulo?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TituloDto>> GetFilteredAsync(string? indexador, bool? vencido, CancellationToken cancellationToken);
}
