using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecosQuery(Guid TituloId, DateOnly? DataInicio, DateOnly? DataFim) : IRequest<Result<IReadOnlyCollection<PrecoTaxaDto>>>;
