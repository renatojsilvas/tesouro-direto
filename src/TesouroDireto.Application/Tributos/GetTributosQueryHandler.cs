using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed class GetTributosQueryHandler(ITributoReadRepository tributoReadRepository)
    : IRequestHandler<GetTributosQuery, Result<IReadOnlyCollection<TributoDto>>>
{
    public async Task<Result<IReadOnlyCollection<TributoDto>>> Handle(GetTributosQuery request, CancellationToken cancellationToken)
    {
        var result = await tributoReadRepository.GetAllAsync(cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        IReadOnlyCollection<TributoDto> dtos = result.Value
            .Select(t => new TributoDto(
                t.Id,
                t.Nome,
                t.BaseCalculo.ToString(),
                t.TipoCalculo.ToString(),
                t.Faixas.Select(f => new FaixaDto(f.DiasMin, f.DiasMax, f.Dia, f.Aliquota)).ToList(),
                t.Ativo,
                t.Ordem,
                t.Cumulativo))
            .ToList();

        return Result<IReadOnlyCollection<TributoDto>>.Success(dtos);
    }
}
