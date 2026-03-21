using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Titulos;

public interface ITituloReadRepository
{
    Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(string? indexador, bool? vencido, CancellationToken cancellationToken);
}
