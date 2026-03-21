using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class PrecoTaxaWriteRepository(AppDbContext dbContext) : IPrecoTaxaWriteRepository
{
    public async Task<Result> AddAsync(PrecoTaxa precoTaxa, CancellationToken cancellationToken)
    {
        await dbContext.PrecosTaxas.AddAsync(precoTaxa, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AddRangeAsync(IReadOnlyCollection<PrecoTaxa> precosTaxas, CancellationToken cancellationToken)
    {
        await dbContext.PrecosTaxas.AddRangeAsync(precosTaxas, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<bool>> ExistsAsync(Guid tituloId, DataBase dataBase, CancellationToken cancellationToken)
    {
        var exists = await dbContext.PrecosTaxas
            .AnyAsync(p => p.TituloId == tituloId && p.DataBase == dataBase, cancellationToken);

        return Result<bool>.Success(exists);
    }

    public async Task<Result<IReadOnlyCollection<DateOnly>>> GetExistingDatasBaseAsync(Guid tituloId, CancellationToken cancellationToken)
    {
        var dates = await dbContext.PrecosTaxas
            .Where(p => p.TituloId == tituloId)
            .Select(p => p.DataBase.Value)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyCollection<DateOnly>>.Success(dates);
    }
}
