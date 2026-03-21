using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Feriados;

public sealed record ImportFeriadosCommand : IRequest<Result<ImportFeriadosResult>>;
