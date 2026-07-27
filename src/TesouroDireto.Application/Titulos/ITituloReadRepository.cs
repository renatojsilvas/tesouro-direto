using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Titulos;

public interface ITituloReadRepository
{
    Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(string? indexador, bool? vencido, CancellationToken cancellationToken);
    Task<Result<Guid>> GetIdByNomeAsync(string nome, CancellationToken cancellationToken);
    Task<Result<Guid>> GetIdByCodigoAsync(string codigo, CancellationToken cancellationToken);
}
