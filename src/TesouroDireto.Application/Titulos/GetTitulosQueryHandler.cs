using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public sealed class GetTitulosQueryHandler(ITituloReadRepository tituloReadRepository)
    : IRequestHandler<GetTitulosQuery, Result<IReadOnlyCollection<TituloDto>>>
{
    public async Task<Result<IReadOnlyCollection<TituloDto>>> Handle(GetTitulosQuery request, CancellationToken cancellationToken)
    {
        if (request.Indexador is not null)
        {
            var indexadorResult = Indexador.FromName(request.Indexador);
            if (indexadorResult.IsFailure)
            {
                return indexadorResult.Error;
            }
        }

        return await tituloReadRepository.GetFilteredAsync(
            request.Indexador,
            request.Vencido,
            cancellationToken);
    }
}
