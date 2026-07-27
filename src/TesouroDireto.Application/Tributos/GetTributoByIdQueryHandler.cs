using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed class GetTributoByIdQueryHandler(ITributoReadRepository tributoReadRepository)
    : IRequestHandler<GetTributoByIdQuery, Result<TributoDto>>
{
    public async Task<Result<TributoDto>> Handle(GetTributoByIdQuery request, CancellationToken cancellationToken)
    {
        var result = await tributoReadRepository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var tributo = result.Value;

        return new TributoDto(
            tributo.Id,
            tributo.Nome,
            tributo.BaseCalculo.ToString(),
            tributo.TipoCalculo.ToString(),
            tributo.Faixas.Select(f => new FaixaDto(f.DiasMin, f.DiasMax, f.Dia, f.Aliquota)).ToList(),
            tributo.Ativo,
            tributo.Ordem,
            tributo.Cumulativo);
    }
}
