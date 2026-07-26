using MediatR;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecoAtualByCodigoQueryHandler(
    ITituloReadRepository tituloReadRepository,
    IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecoAtualByCodigoQuery, Result<PrecoTaxaDto>>
{
    public async Task<Result<PrecoTaxaDto>> Handle(GetPrecoAtualByCodigoQuery request, CancellationToken cancellationToken)
    {
        var tituloResult = await tituloReadRepository.GetByCodigoAsync(request.Codigo, cancellationToken);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        return await precoTaxaReadRepository.GetLatestByTituloIdAsync(tituloResult.Value.Id, cancellationToken);
    }
}
