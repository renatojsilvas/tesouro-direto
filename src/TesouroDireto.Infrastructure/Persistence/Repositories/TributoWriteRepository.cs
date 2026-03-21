using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TributoWriteRepository(AppDbContext dbContext) : ITributoWriteRepository
{
    public async Task AddAsync(Tributo tributo, CancellationToken cancellationToken)
    {
        await dbContext.Tributos.AddAsync(tributo, cancellationToken);
    }

    public void Update(Tributo tributo)
    {
        dbContext.Tributos.Update(tributo);
    }
}
