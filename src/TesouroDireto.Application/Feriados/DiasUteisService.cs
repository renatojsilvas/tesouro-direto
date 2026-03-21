using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.DiasUteis;

namespace TesouroDireto.Application.Feriados;

public sealed class DiasUteisService(IFeriadoReadRepository feriadoReadRepository) : IDiasUteisService
{
    private static readonly DiasUteisCalculator Calculator = new();

    public async Task<Result<int>> CalcularDiasUteisAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken)
    {
        var feriadosResult = await feriadoReadRepository.GetAllDatasAsync(cancellationToken);
        if (feriadosResult.IsFailure)
        {
            return feriadosResult.Error;
        }

        var diasUteis = Calculator.Calcular(inicio, fim, feriadosResult.Value);
        return diasUteis;
    }
}
