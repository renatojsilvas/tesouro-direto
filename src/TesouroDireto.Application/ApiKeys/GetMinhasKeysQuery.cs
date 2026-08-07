using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed record GetMinhasKeysQuery(Guid DonoUsuarioId) : IRequest<Result<IReadOnlyCollection<ApiKeyDto>>>;
