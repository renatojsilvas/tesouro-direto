using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class ApiKeyWriteRepository(AppDbContext dbContext) : IApiKeyWriteRepository
{
    public async Task<Result> AddAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        var jaExiste = await dbContext.ApiKeys
            .AnyAsync(k => k.Hash == apiKey.Hash, cancellationToken);

        if (jaExiste)
        {
            return Result.Failure(ApiKeyErrors.AlreadyExists);
        }

        await dbContext.ApiKeys.AddAsync(apiKey, cancellationToken);
        return Result.Success();
    }

    public Task<Result> UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        dbContext.ApiKeys.Update(apiKey);
        return Task.FromResult(Result.Success());
    }

    public async Task<Result<ApiKey>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var apiKey = await dbContext.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

        return apiKey is not null
            ? Result<ApiKey>.Success(apiKey)
            : Result<ApiKey>.Failure(ApiKeyErrors.NotFound);
    }
}
