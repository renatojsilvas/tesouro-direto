using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecoAtualQueryHandler(IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecoAtualQuery, Result<PrecoTaxaDto>>
{
    public async Task<Result<PrecoTaxaDto>> Handle(GetPrecoAtualQuery request, CancellationToken cancellationToken)
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

        return await precoTaxaReadRepository.GetLatestByTituloIdAsync(request.TituloId, cancellationToken);
    }
}
