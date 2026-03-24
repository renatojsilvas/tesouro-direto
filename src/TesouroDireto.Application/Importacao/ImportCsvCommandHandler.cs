using MediatR;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Importacao;

public sealed class ImportCsvCommandHandler(
    ICsvImportService csvImportService,
    ITituloWriteRepository tituloWriteRepository,
    IPrecoTaxaWriteRepository precoTaxaWriteRepository,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    ILogger<ImportCsvCommandHandler> logger)
    : IRequestHandler<ImportCsvCommand, Result<ImportResult>>
{
    private const int BatchSize = 1000;

    public async Task<Result<ImportResult>> Handle(ImportCsvCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting CSV import");
        var tituloCache = new Dictionary<(string, DateOnly), Titulo>();
        var existingDatesCache = new Dictionary<Guid, HashSet<DateOnly>>();
        var batch = new List<PrecoTaxa>(BatchSize);

        var titulosCriados = 0;
        var precosInseridos = 0;
        var precosIgnorados = 0;
        var linhasComErro = 0;

        await foreach (var recordResult in csvImportService.GetRecordsAsync(cancellationToken))
        {
            if (recordResult.IsFailure)
            {
                linhasComErro++;
                continue;
            }

            var record = recordResult.Value;

            var tituloResult = await GetOrCreateTituloAsync(
                record, tituloCache, cancellationToken);

            if (tituloResult.IsFailure)
            {
                linhasComErro++;
                continue;
            }

            var (titulo, isNew) = tituloResult.Value;
            if (isNew)
            {
                titulosCriados++;
            }

            var existingDatesResult = await GetExistingDatesAsync(
                titulo.Id, existingDatesCache, cancellationToken);

            if (existingDatesResult.IsFailure)
            {
                linhasComErro++;
                continue;
            }

            var existingDates = existingDatesResult.Value;

            if (existingDates.Contains(record.DataBase))
            {
                precosIgnorados++;
                continue;
            }

            var precoResult = CreatePrecoTaxa(titulo.Id, record);
            if (precoResult.IsFailure)
            {
                linhasComErro++;
                continue;
            }

            batch.Add(precoResult.Value);
            existingDates.Add(record.DataBase);
            precosInseridos++;

            if (batch.Count >= BatchSize)
            {
                await FlushBatchAsync(batch, cancellationToken);
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, cancellationToken);
        }

        cacheInvalidator.InvalidateTitulos();
        cacheInvalidator.InvalidatePrecos();

        var result = new ImportResult(titulosCriados, precosInseridos, precosIgnorados, linhasComErro);

        logger.LogInformation(
            "CSV import completed: {TitulosCriados} titulos created, {PrecosInseridos} precos inserted, {PrecosIgnorados} skipped, {LinhasComErro} errors",
            result.TitulosCriados, result.PrecosInseridos, result.PrecosIgnorados, result.LinhasComErro);

        return result;
    }

    private async Task<Result<(Titulo Titulo, bool IsNew)>> GetOrCreateTituloAsync(
        CsvRecord record,
        Dictionary<(string, DateOnly), Titulo> cache,
        CancellationToken cancellationToken)
    {
        var key = (record.TipoTitulo, record.DataVencimento);

        if (cache.TryGetValue(key, out var cached))
        {
            return (cached, false);
        }

        var tipoResult = TipoTitulo.FromName(record.TipoTitulo);
        if (tipoResult.IsFailure)
        {
            return tipoResult.Error;
        }

        var dataVencimentoResult = DataVencimento.Create(record.DataVencimento);
        if (dataVencimentoResult.IsFailure)
        {
            return dataVencimentoResult.Error;
        }

        var existingResult = await tituloWriteRepository.GetByTipoAndVencimentoAsync(
            tipoResult.Value, dataVencimentoResult.Value, cancellationToken);

        if (existingResult.IsSuccess)
        {
            cache[key] = existingResult.Value;
            return (existingResult.Value, false);
        }

        var tituloResult = Titulo.Create(tipoResult.Value, dataVencimentoResult.Value);
        if (tituloResult.IsFailure)
        {
            return tituloResult.Error;
        }

        var addResult = await tituloWriteRepository.AddAsync(tituloResult.Value, cancellationToken);
        if (addResult.IsFailure)
        {
            return addResult.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        cache[key] = tituloResult.Value;
        return (tituloResult.Value, true);
    }

    private async Task<Result<HashSet<DateOnly>>> GetExistingDatesAsync(
        Guid tituloId,
        Dictionary<Guid, HashSet<DateOnly>> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(tituloId, out var dates))
        {
            return Result<HashSet<DateOnly>>.Success(dates);
        }

        var existingResult = await precoTaxaWriteRepository.GetExistingDatasBaseAsync(
            tituloId, cancellationToken);

        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        var hashSet = new HashSet<DateOnly>(existingResult.Value);
        cache[tituloId] = hashSet;
        return Result<HashSet<DateOnly>>.Success(hashSet);
    }

    private static Result<PrecoTaxa> CreatePrecoTaxa(Guid tituloId, CsvRecord record)
    {
        var dataBaseResult = DataBase.Create(record.DataBase);
        if (dataBaseResult.IsFailure) return dataBaseResult.Error;

        var taxaCompra = CreateTaxaOrNull(record.TaxaCompra);
        var taxaVenda = CreateTaxaOrNull(record.TaxaVenda);
        var puCompra = CreatePuOrNull(record.PuCompra);
        var puVenda = CreatePuOrNull(record.PuVenda);
        var puBase = CreatePuOrNull(record.PuBase);

        return PrecoTaxa.Create(
            tituloId,
            dataBaseResult.Value,
            taxaCompra,
            taxaVenda,
            puCompra,
            puVenda,
            puBase);
    }

    private static Taxa? CreateTaxaOrNull(decimal value) =>
        value == 0 ? null : Taxa.Create(value);

    private static PrecoUnitario? CreatePuOrNull(decimal value) =>
        value == 0 ? null : PrecoUnitario.Create(value).Value;

    private async Task FlushBatchAsync(List<PrecoTaxa> batch, CancellationToken cancellationToken)
    {
        await precoTaxaWriteRepository.AddRangeAsync(batch, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        batch.Clear();
    }
}
