using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public interface IPrecoTaxaReadRepository
{
    Task<Result<bool>> TituloExistsAsync(Guid tituloId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> GetByTituloIdAsync(Guid tituloId, DateOnly? dataInicio, DateOnly? dataFim, CancellationToken cancellationToken);
    Task<Result<PrecoTaxaDto>> GetLatestByTituloIdAsync(Guid tituloId, CancellationToken cancellationToken);
}
