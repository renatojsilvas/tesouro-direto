    using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using TesouroDireto.Application.Feriados;

namespace TesouroDireto.Infrastructure.Feriados;

[DisallowConcurrentExecution]
public sealed class FeriadoImportJob(ISender sender, ILogger<FeriadoImportJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Scheduled feriados import job started");

        var result = await sender.Send(new ImportFeriadosCommand(), context.CancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Scheduled feriados import completed: {FeriadosImportados} imported, {FeriadosIgnorados} skipped",
                result.Value.FeriadosImportados, result.Value.FeriadosIgnorados);
        }
        else
        {
            logger.LogError("Scheduled feriados import failed: {ErrorCode} - {ErrorDescription}",
                result.Error.Code, result.Error.Description);
        }
    }
}
