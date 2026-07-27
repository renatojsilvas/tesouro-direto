using MediatR;
using TesouroDireto.Application.Common;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecosByCodigoQuery(string Codigo, DateOnly? DataInicio, DateOnly? DataFim, int? Page, int? PageSize)
    : IRequest<Result<PagedResult<PrecoTaxaDto>>>;
