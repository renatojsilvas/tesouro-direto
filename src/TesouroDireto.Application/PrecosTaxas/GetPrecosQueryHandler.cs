using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecosQueryHandler(IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecosQuery, Result<IReadOnlyCollection<PrecoTaxaDto>>>
{
    public async Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> Handle(GetPrecosQuery request, CancellationToken cancellationToken)
    {
        if (request.TituloId == Guid.Empty)
        {
            return TituloErrors.NotFound;
        }

        var existsResult = await precoTaxaReadRepository.TituloExistsAsync(request.TituloId, cancellationToken);
        if (existsResult.IsFailure)
        {
            return existsResult.Error;
        }

        if (!existsResult.Value)
        {
            return TituloErrors.NotFound;
        }

        return await precoTaxaReadRepository.GetByTituloIdAsync(
            request.TituloId,
            request.DataInicio,
            request.DataFim,
            cancellationToken);
    }
}
