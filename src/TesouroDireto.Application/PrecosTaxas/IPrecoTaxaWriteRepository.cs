using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Application.PrecosTaxas;

public interface IPrecoTaxaWriteRepository
{
    Task AddAsync(PrecoTaxa precoTaxa, CancellationToken cancellationToken);
    Task AddRangeAsync(IReadOnlyCollection<PrecoTaxa> precosTaxas, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid tituloId, DataBase dataBase, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DateOnly>> GetExistingDatasBaseAsync(Guid tituloId, CancellationToken cancellationToken);
}
