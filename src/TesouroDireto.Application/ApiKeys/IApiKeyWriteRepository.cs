using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public interface IApiKeyWriteRepository
{
    Task<Result> AddAsync(ApiKey apiKey, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken);
    Task<Result<ApiKey>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
