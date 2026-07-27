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
        var tituloIdResult = await tituloReadRepository.GetIdByCodigoAsync(request.Codigo, cancellationToken);
        if (tituloIdResult.IsFailure)
        {
            return tituloIdResult.Error;
        }

        return await precoTaxaReadRepository.GetByTituloIdAsync(
            tituloIdResult.Value,
            request.DataInicio,
            request.DataFim,
            cancellationToken);
    }
}
