using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed record RevokeApiKeyCommand(Guid KeyId, Guid DonoUsuarioId) : IRequest<Result>;
