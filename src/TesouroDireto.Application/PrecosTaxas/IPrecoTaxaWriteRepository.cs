using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Application.PrecosTaxas;

public interface IPrecoTaxaWriteRepository
{
    Task<Result> AddAsync(PrecoTaxa precoTaxa, CancellationToken cancellationToken);
    Task<Result> AddRangeAsync(IReadOnlyCollection<PrecoTaxa> precosTaxas, CancellationToken cancellationToken);
    Task<Result<bool>> ExistsAsync(Guid tituloId, DataBase dataBase, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<DateOnly>>> GetExistingDatasBaseAsync(Guid tituloId, CancellationToken cancellationToken);
}
