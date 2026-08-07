using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed class GetMinhasKeysQueryHandler(IApiKeyReadRepository apiKeyReadRepository)
    : IRequestHandler<GetMinhasKeysQuery, Result<IReadOnlyCollection<ApiKeyDto>>>
{
    public Task<Result<IReadOnlyCollection<ApiKeyDto>>> Handle(GetMinhasKeysQuery request, CancellationToken cancellationToken) =>
        apiKeyReadRepository.ListByDonoAsync(request.DonoUsuarioId, cancellationToken);
}
