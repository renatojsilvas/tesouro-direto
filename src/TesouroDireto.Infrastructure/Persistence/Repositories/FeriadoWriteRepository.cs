using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class FeriadoWriteRepository(AppDbContext dbContext) : IFeriadoWriteRepository
{
    public async Task<Result> AddRangeAsync(IReadOnlyCollection<Feriado> feriados, CancellationToken cancellationToken)
    {
        await dbContext.Feriados.AddRangeAsync(feriados, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyCollection<DateOnly>>> GetExistingDatasAsync(CancellationToken cancellationToken)
    {
        var datas = await dbContext.Feriados
            .Select(f => f.Data.Value)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyCollection<DateOnly>>.Success(datas);
    }
}
