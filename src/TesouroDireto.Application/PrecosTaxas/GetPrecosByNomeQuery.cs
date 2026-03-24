using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecosByNomeQuery(string Nome, DateOnly? DataInicio, DateOnly? DataFim)
    : IRequest<Result<IReadOnlyCollection<PrecoTaxaDto>>>;
