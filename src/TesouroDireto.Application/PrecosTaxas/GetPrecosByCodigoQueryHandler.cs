using MediatR;
using TesouroDireto.Application.Common;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecosByCodigoQueryHandler(
    ITituloReadRepository tituloReadRepository,
    IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecosByCodigoQuery, Result<PagedResult<PrecoTaxaDto>>>
{
    public async Task<Result<PagedResult<PrecoTaxaDto>>> Handle(
        GetPrecosByCodigoQuery request,
        CancellationToken cancellationToken)
    {
        var tituloIdResult = await tituloReadRepository.GetIdByCodigoAsync(request.Codigo, cancellationToken);
        if (tituloIdResult.IsFailure)
        {
            return tituloIdResult.Error;
        }

        var precosResult = await precoTaxaReadRepository.GetByTituloIdAsync(
            tituloIdResult.Value,
            request.DataInicio,
            request.DataFim,
            cancellationToken);

        if (precosResult.IsFailure)
        {
            return precosResult.Error;
        }

        var precos = precosResult.Value;

        if (request.Page is null)
        {
            return new PagedResult<PrecoTaxaDto>(precos.ToList(), precos.Count);
        }

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize);
        var items = precos.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<PrecoTaxaDto>(items, precos.Count);
    }
}
