using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public interface IApiKeyReadRepository
{
    Task<Result<ApiKeyDto>> GetByHashAsync(string hash, CancellationToken cancellationToken);
    Task<Result<ApiKeyDto>> GetActiveByHashAsync(string hash, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<ApiKeyDto>>> ListByDonoAsync(Guid donoUsuarioId, CancellationToken cancellationToken);
}
