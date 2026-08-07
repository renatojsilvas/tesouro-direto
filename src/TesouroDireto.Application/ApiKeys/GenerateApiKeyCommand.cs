using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed record GenerateApiKeyCommand(Guid DonoUsuarioId, string Nome) : IRequest<Result<GeneratedApiKeyDto>>;
