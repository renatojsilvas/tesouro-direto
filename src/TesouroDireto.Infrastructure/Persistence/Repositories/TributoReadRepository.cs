using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TributoReadRepository(AppDbContext dbContext) : ITributoReadRepository
{
    public async Task<Result<IReadOnlyCollection<Tributo>>> GetAllAsync(CancellationToken cancellationToken)
    {
        var tributos = await dbContext.Tributos
            .OrderBy(t => t.Ordem)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyCollection<Tributo>>.Success(tributos);
    }

    public async Task<Result<Tributo?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var tributo = await dbContext.Tributos
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return Result<Tributo?>.Success(tributo);
    }

    public async Task<Result<IReadOnlyCollection<Tributo>>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken)
    {
        var tributos = await dbContext.Tributos
            .Where(t => t.Ativo)
            .OrderBy(t => t.Ordem)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyCollection<Tributo>>.Success(tributos);
    }
}
