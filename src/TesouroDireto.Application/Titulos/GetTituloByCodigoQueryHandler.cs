using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public sealed class GetTituloByCodigoQueryHandler(ITituloReadRepository tituloReadRepository)
    : IRequestHandler<GetTituloByCodigoQuery, Result<TituloDto>>
{
    public async Task<Result<TituloDto>> Handle(GetTituloByCodigoQuery request, CancellationToken cancellationToken)
    {
        if (!TituloCodigo.TryParseDate(request.Codigo, out _))
        {
            return TituloErrors.InvalidCodigo;
        }

        var result = await tituloReadRepository.GetFilteredAsync(null, null, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var titulo = result.Value.FirstOrDefault(t =>
            string.Equals(t.Codigo, request.Codigo, StringComparison.Ordinal));

        return titulo is not null
            ? Result<TituloDto>.Success(titulo)
            : TituloErrors.NotFound;
    }
}
