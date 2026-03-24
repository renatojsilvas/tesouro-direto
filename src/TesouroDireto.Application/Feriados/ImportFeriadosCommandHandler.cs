using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Application.Feriados;

public sealed class ImportFeriadosCommandHandler(
    IFeriadoImportService importService,
    IFeriadoWriteRepository writeRepository,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<ImportFeriadosCommand, Result<ImportFeriadosResult>>
{
    public async Task<Result<ImportFeriadosResult>> Handle(ImportFeriadosCommand request, CancellationToken cancellationToken)
    {
        var existingResult = await writeRepository.GetExistingDatasAsync(cancellationToken);
        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        var existingDatas = new HashSet<DateOnly>(existingResult.Value);
        var newFeriados = new List<Feriado>();
        var ignorados = 0;

        await foreach (var record in importService.GetFeriadosAsync(cancellationToken))
        {
            if (existingDatas.Contains(record.Data))
            {
                ignorados++;
                continue;
            }

            var dataResult = DataFeriado.Create(record.Data);
            if (dataResult.IsFailure)
            {
                continue;
            }

            var feriadoResult = Feriado.Create(dataResult.Value, record.Descricao);
            if (feriadoResult.IsFailure)
            {
                continue;
            }

            newFeriados.Add(feriadoResult.Value);
            existingDatas.Add(record.Data);
        }

        if (newFeriados.Count > 0)
        {
            var addResult = await writeRepository.AddRangeAsync(newFeriados, cancellationToken);
            if (addResult.IsFailure)
            {
                return addResult.Error;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        cacheInvalidator.InvalidateFeriados();

        return new ImportFeriadosResult(newFeriados.Count, ignorados);
    }
}
