using MediatR;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecoAtualByNomeQueryHandler(
    ITituloReadRepository tituloReadRepository,
    IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecoAtualByNomeQuery, Result<PrecoTaxaDto>>
{
    public async Task<Result<PrecoTaxaDto>> Handle(GetPrecoAtualByNomeQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
        {
            return TituloErrors.InvalidNome;
        }

        var tituloIdResult = await tituloReadRepository.GetIdByNomeAsync(request.Nome, cancellationToken);
        if (tituloIdResult.IsFailure)
        {
            return tituloIdResult.Error;
        }

        return await precoTaxaReadRepository.GetLatestByTituloIdAsync(tituloIdResult.Value, cancellationToken);
    }
}
