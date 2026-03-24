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

        var tituloResult = await tituloReadRepository.GetByNomeAsync(request.Nome, cancellationToken);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        return await precoTaxaReadRepository.GetLatestByTituloIdAsync(tituloResult.Value.Id, cancellationToken);
    }
}
