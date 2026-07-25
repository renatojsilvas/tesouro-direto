using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Importacao;

namespace TesouroDireto.Infrastructure.CsvImport;

[DisallowConcurrentExecution]
public sealed class CsvImportJob(ISender sender, ILogger<CsvImportJob> logger, IBusinessMetrics metrics) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Scheduled CSV import job started");

        var result = await sender.Send(new ImportCsvCommand(), context.CancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Scheduled CSV import completed: {TitulosCriados} titulos, {PrecosInseridos} precos, {PrecosIgnorados} skipped, {LinhasComErro} errors",
                result.Value.TitulosCriados, result.Value.PrecosInseridos,
                result.Value.PrecosIgnorados, result.Value.LinhasComErro);
            metrics.RecordImportRun("success");
        }
        else
        {
            logger.LogError("Scheduled CSV import failed: {ErrorCode} - {ErrorDescription}",
                result.Error.Code, result.Error.Description);
            metrics.RecordImportRun("failure");
        }
    }
}
