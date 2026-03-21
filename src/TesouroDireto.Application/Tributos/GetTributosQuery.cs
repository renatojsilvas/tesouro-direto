using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed record GetTributosQuery : IRequest<Result<IReadOnlyCollection<TributoDto>>>;
