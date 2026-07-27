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
        var tituloIdResult = await tituloReadRepository.GetIdByCodigoAsync(request.Codigo, cancellationToken);
        if (tituloIdResult.IsFailure)
        {
            return tituloIdResult.Error;
        }

        return await precoTaxaReadRepository.GetLatestByTituloIdAsync(tituloIdResult.Value, cancellationToken);
    }
}
