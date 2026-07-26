using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecosByCodigoQuery(string Codigo, DateOnly? DataInicio, DateOnly? DataFim)
    : IRequest<Result<IReadOnlyCollection<PrecoTaxaDto>>>;
