using MediatR;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecosByNomeQueryHandler(
    ITituloReadRepository tituloReadRepository,
    IPrecoTaxaReadRepository precoTaxaReadRepository)
    : IRequestHandler<GetPrecosByNomeQuery, Result<IReadOnlyCollection<PrecoTaxaDto>>>
{
    public async Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> Handle(
        GetPrecosByNomeQuery request,
        CancellationToken cancellationToken)
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

        return await precoTaxaReadRepository.GetByTituloIdAsync(
            tituloResult.Value.Id,
            request.DataInicio,
            request.DataFim,
            cancellationToken);
    }
}
