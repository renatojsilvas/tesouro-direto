using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Titulos;

public sealed record GetTituloByCodigoQuery(string Codigo) : IRequest<Result<TituloDto>>;
