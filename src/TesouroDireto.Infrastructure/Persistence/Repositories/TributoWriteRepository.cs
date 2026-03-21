using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TributoWriteRepository(AppDbContext dbContext) : ITributoWriteRepository
{
    public async Task<Result> AddAsync(Tributo tributo, CancellationToken cancellationToken)
    {
        await dbContext.Tributos.AddAsync(tributo, cancellationToken);
        return Result.Success();
    }

    public Task<Result> UpdateAsync(Tributo tributo, CancellationToken cancellationToken)
    {
        dbContext.Tributos.Update(tributo);
        return Task.FromResult(Result.Success());
    }
}
