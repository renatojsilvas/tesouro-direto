using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TributoReadRepository(AppDbContext dbContext) : ITributoReadRepository
{
    public async Task<IReadOnlyCollection<Tributo>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Tributos
            .OrderBy(t => t.Ordem)
            .ToListAsync(cancellationToken);
    }

    public async Task<Tributo?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Tributos
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Tributo>> GetAtivosOrdenadosAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Tributos
            .Where(t => t.Ativo)
            .OrderBy(t => t.Ordem)
            .ToListAsync(cancellationToken);
    }
}
