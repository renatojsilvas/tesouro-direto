using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class PrecoTaxaWriteRepository(AppDbContext dbContext) : IPrecoTaxaWriteRepository
{
    public async Task AddAsync(PrecoTaxa precoTaxa, CancellationToken cancellationToken)
    {
        await dbContext.PrecosTaxas.AddAsync(precoTaxa, cancellationToken);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<PrecoTaxa> precosTaxas, CancellationToken cancellationToken)
    {
        await dbContext.PrecosTaxas.AddRangeAsync(precosTaxas, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid tituloId, DataBase dataBase, CancellationToken cancellationToken)
    {
        return await dbContext.PrecosTaxas
            .AnyAsync(p => p.TituloId == tituloId && p.DataBase == dataBase, cancellationToken);
    }
}
