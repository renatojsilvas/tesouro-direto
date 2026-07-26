using MediatR;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecosByCodigoQueryHandler(
    ITituloReadRepository tituloReadRepository,
    IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecosByCodigoQuery, Result<IReadOnlyCollection<PrecoTaxaDto>>>
{
    public async Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> Handle(
        GetPrecosByCodigoQuery request,
        CancellationToken cancellationToken)
    {
        var tituloResult = await tituloReadRepository.GetByCodigoAsync(request.Codigo, cancellationToken);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        return await precoTaxaReadRepository.GetByTituloIdAsync(
            tituloResult.Value.Id,
            request.DataInicio,
            request.DataFim,
            cancellationToken);
    }
}
