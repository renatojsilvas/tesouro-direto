using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Application.Tests.Feriados;

public sealed class ImportFeriadosCommandHandlerTests
{
    private readonly IFeriadoImportService _importService = Substitute.For<IFeriadoImportService>();
    private readonly IFeriadoWriteRepository _writeRepo = Substitute.For<IFeriadoWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ImportFeriadosCommandHandler _handler;

    public ImportFeriadosCommandHandlerTests()
    {
        _handler = new ImportFeriadosCommandHandler(_importService, _writeRepo, _unitOfWork);

        _writeRepo.AddRangeAsync(Arg.Any<IReadOnlyCollection<Feriado>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WithNewFeriados_ShouldImportAll()
    {
        var records = new List<FeriadoRecord>
        {
            new(new DateOnly(2024, 12, 25), "Natal"),
            new(new DateOnly(2024, 1, 1), "Confraternização Universal")
        };
        _importService.GetFeriadosAsync(Arg.Any<CancellationToken>())
            .Returns(records.ToAsyncEnumerable());
        _writeRepo.GetExistingDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));

        var result = await _handler.Handle(new ImportFeriadosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FeriadosImportados.Should().Be(2);
        result.Value.FeriadosIgnorados.Should().Be(0);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithExistingFeriados_ShouldSkipDuplicates()
    {
        var records = new List<FeriadoRecord>
        {
            new(new DateOnly(2024, 12, 25), "Natal"),
            new(new DateOnly(2024, 1, 1), "Confraternização Universal")
        };
        _importService.GetFeriadosAsync(Arg.Any<CancellationToken>())
            .Returns(records.ToAsyncEnumerable());
        _writeRepo.GetExistingDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(
                new[] { new DateOnly(2024, 12, 25) } as IReadOnlyCollection<DateOnly>));

        var result = await _handler.Handle(new ImportFeriadosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FeriadosImportados.Should().Be(1);
        result.Value.FeriadosIgnorados.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithAllExisting_ShouldNotCallSave()
    {
        var records = new List<FeriadoRecord>
        {
            new(new DateOnly(2024, 12, 25), "Natal")
        };
        _importService.GetFeriadosAsync(Arg.Any<CancellationToken>())
            .Returns(records.ToAsyncEnumerable());
        _writeRepo.GetExistingDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(
                new[] { new DateOnly(2024, 12, 25) } as IReadOnlyCollection<DateOnly>));

        var result = await _handler.Handle(new ImportFeriadosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FeriadosImportados.Should().Be(0);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGetExistingFails_ShouldReturnFailure()
    {
        _importService.GetFeriadosAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<FeriadoRecord>());
        _writeRepo.GetExistingDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Failure(
                new Error("Repository.Error", "Database error")));

        var result = await _handler.Handle(new ImportFeriadosCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }
}
