using System.Globalization;
using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed class GetPrecosPorDataQueryHandler(
    IPrecoTaxaReadRepository precoTaxaReadRepository,
    TimeProvider timeProvider)
    : IRequestHandler<GetPrecosPorDataQuery, Result<IReadOnlyList<PrecoTaxaDiaDto>>>
{
    public async Task<Result<IReadOnlyList<PrecoTaxaDiaDto>>> Handle(
        GetPrecosPorDataQuery request,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(
            request.DataBase,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dataBase))
        {
            return PrecoTaxaErrors.DataBaseInvalida;
        }

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (dataBase > hoje)
        {
            return PrecoTaxaErrors.DataBaseFutura;
        }

        return await precoTaxaReadRepository.GetByDataBaseAsync(dataBase, cancellationToken);
    }
}
