using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Tests.Importacao;

public sealed class ImportCsvCommandHandlerTests
{
    private readonly ICsvImportService _csvImportService = Substitute.For<ICsvImportService>();
    private readonly ITituloWriteRepository _tituloWriteRepository = Substitute.For<ITituloWriteRepository>();
    private readonly IPrecoTaxaWriteRepository _precoTaxaWriteRepository = Substitute.For<IPrecoTaxaWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ImportCsvCommandHandler _handler;

    public ImportCsvCommandHandlerTests()
    {
        _handler = new ImportCsvCommandHandler(
            _csvImportService,
            _tituloWriteRepository,
            _precoTaxaWriteRepository,
            _unitOfWork,
            Substitute.For<ICacheInvalidator>(),
            Substitute.For<ILogger<ImportCsvCommandHandler>>());

        _tituloWriteRepository
            .AddAsync(Arg.Any<Titulo>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _precoTaxaWriteRepository
            .AddRangeAsync(Arg.Any<IReadOnlyCollection<Domain.PrecosTaxas.PrecoTaxa>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WithNewTituloAndPrecos_ShouldImportSuccessfully()
    {
        var records = new[]
        {
            Result<CsvRecord>.Success(new CsvRecord(
                "Tesouro Prefixado", new DateOnly(2025, 1, 1), new DateOnly(2023, 1, 2),
                13.12m, 13.18m, 756.43m, 755.39m, 756.43m))
        };

        _csvImportService
            .GetRecordsAsync(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(records));

        _tituloWriteRepository
            .GetByTipoAndVencimentoAsync(Arg.Any<TipoTitulo>(), Arg.Any<DataVencimento>(), Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Failure(new Error("Titulo.NotFound", "Titulo was not found.")));

        _precoTaxaWriteRepository
            .GetExistingDatasBaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));

        var result = await _handler.Handle(new ImportCsvCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TitulosCriados.Should().Be(1);
        result.Value.PrecosInseridos.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithExistingTitulo_ShouldReuseAndNotCreateNew()
    {
        var existingTitulo = Titulo.Create(
            TipoTitulo.TesouroPrefixado,
            DataVencimento.Create(new DateOnly(2025, 1, 1)).Value).Value;

        var records = new[]
        {
            Result<CsvRecord>.Success(new CsvRecord(
                "Tesouro Prefixado", new DateOnly(2025, 1, 1), new DateOnly(2023, 1, 2),
                13.12m, 13.18m, 756.43m, 755.39m, 756.43m))
        };

        _csvImportService
            .GetRecordsAsync(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(records));

        _tituloWriteRepository
            .GetByTipoAndVencimentoAsync(Arg.Any<TipoTitulo>(), Arg.Any<DataVencimento>(), Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(existingTitulo));

        _precoTaxaWriteRepository
            .GetExistingDatasBaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));

        var result = await _handler.Handle(new ImportCsvCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TitulosCriados.Should().Be(0);
        result.Value.PrecosInseridos.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithExistingPrecoTaxa_ShouldSkip()
    {
        var existingTitulo = Titulo.Create(
            TipoTitulo.TesouroPrefixado,
            DataVencimento.Create(new DateOnly(2025, 1, 1)).Value).Value;

        var records = new[]
        {
            Result<CsvRecord>.Success(new CsvRecord(
                "Tesouro Prefixado", new DateOnly(2025, 1, 1), new DateOnly(2023, 1, 2),
                13.12m, 13.18m, 756.43m, 755.39m, 756.43m))
        };

        _csvImportService
            .GetRecordsAsync(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(records));

        _tituloWriteRepository
            .GetByTipoAndVencimentoAsync(Arg.Any<TipoTitulo>(), Arg.Any<DataVencimento>(), Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(existingTitulo));

        _precoTaxaWriteRepository
            .GetExistingDatasBaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(new[] { new DateOnly(2023, 1, 2) } as IReadOnlyCollection<DateOnly>));

        var result = await _handler.Handle(new ImportCsvCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PrecosInseridos.Should().Be(0);
        result.Value.PrecosIgnorados.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithInvalidCsvRecord_ShouldCountAsError()
    {
        var records = new[]
        {
            Result<CsvRecord>.Failure(new Error("CsvImport.InvalidLine", "Invalid line"))
        };

        _csvImportService
            .GetRecordsAsync(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(records));

        var result = await _handler.Handle(new ImportCsvCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LinhasComErro.Should().Be(1);
        result.Value.PrecosInseridos.Should().Be(0);
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
#pragma warning restore CS1998
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }
}
