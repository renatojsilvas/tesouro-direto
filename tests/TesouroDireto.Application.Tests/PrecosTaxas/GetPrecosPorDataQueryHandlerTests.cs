using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecosPorDataQueryHandlerTests
{
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly GetPrecosPorDataQueryHandler _handler;

    public GetPrecosPorDataQueryHandlerTests()
    {
        _handler = new GetPrecosPorDataQueryHandler(_precoReadRepo, _timeProvider);
    }

    [Fact]
    public async Task Handle_WithNullDataBase_ShouldReturnDataBaseInvalidaAndNotCallRepository()
    {
        var result = await _handler.Handle(new GetPrecosPorDataQuery(null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PrecoTaxaErrors.DataBaseInvalida.Code);
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _precoReadRepo.DidNotReceive().GetByDataBaseAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("xxx")]
    [InlineData("31-12-2025")]
    [InlineData("2025-13-01")]
    [InlineData("2025/12/31")]
    public async Task Handle_WithMalformedDataBase_ShouldReturnDataBaseInvalidaAndNotCallRepository(string dataBase)
    {
        var result = await _handler.Handle(new GetPrecosPorDataQuery(dataBase), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PrecoTaxaErrors.DataBaseInvalida.Code);
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _precoReadRepo.DidNotReceive().GetByDataBaseAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithFutureDataBase_ShouldReturnDataBaseFuturaAndNotCallRepository()
    {
        // Relógio fake em 2026-01-01; dataBase pedida é o dia seguinte.
        var result = await _handler.Handle(new GetPrecosPorDataQuery("2026-01-02"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PrecoTaxaErrors.DataBaseFutura.Code);
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _precoReadRepo.DidNotReceive().GetByDataBaseAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDataBaseEqualToToday_ShouldCallRepositoryAndReturnItsResult()
    {
        // Prova que a comparação é ">" e não ">=": hoje não é "futuro".
        var precosHoje = new List<PrecoTaxaDiaDto>
        {
            new("tesouro-selic-2029-03-01", "2026-01-01", 13.00m, 13.10m, 100.00m, 99.00m, 100.00m),
        };
        _precoReadRepo.GetByDataBaseAsync(new DateOnly(2026, 1, 1), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PrecoTaxaDiaDto>>.Success(precosHoje));

        var result = await _handler.Handle(new GetPrecosPorDataQuery("2026-01-01"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(precosHoje);
        await _precoReadRepo.Received(1).GetByDataBaseAsync(new DateOnly(2026, 1, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidPastDataBase_ShouldCallRepositoryWithParsedDateAndReturnResultIntact()
    {
        var parsedDate = new DateOnly(2025, 12, 31);
        _precoReadRepo.GetByDataBaseAsync(parsedDate, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PrecoTaxaDiaDto>>.Success(new List<PrecoTaxaDiaDto>()));

        var result = await _handler.Handle(new GetPrecosPorDataQuery("2025-12-31"), CancellationToken.None);

        // Lista vazia é sucesso, não NotFound.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await _precoReadRepo.Received(1).GetByDataBaseAsync(parsedDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRepositoryFails_ShouldPropagateFailureWithoutTranslation()
    {
        var repositoryError = new Error("PrecoTaxa.SomeInfraFailure", "boom", ErrorType.Conflict);
        _precoReadRepo.GetByDataBaseAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PrecoTaxaDiaDto>>.Failure(repositoryError));

        var result = await _handler.Handle(new GetPrecosPorDataQuery("2025-12-31"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(repositoryError);
    }
}
