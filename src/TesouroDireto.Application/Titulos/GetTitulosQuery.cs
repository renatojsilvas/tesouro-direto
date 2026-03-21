using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Titulos;

public sealed record GetTitulosQuery(string? Indexador, bool? Vencido) : IRequest<Result<IReadOnlyCollection<TituloDto>>>;
