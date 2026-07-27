using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed record GetTributoByIdQuery(Guid Id) : IRequest<Result<TributoDto>>;
