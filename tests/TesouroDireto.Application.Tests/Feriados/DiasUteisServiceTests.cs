using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.Feriados;

public sealed class DiasUteisServiceTests
{
    private readonly IFeriadoReadRepository _readRepo = Substitute.For<IFeriadoReadRepository>();
    private readonly DiasUteisService _service;

    public DiasUteisServiceTests()
    {
        _service = new DiasUteisService(_readRepo);
    }

    [Fact]
    public async Task CalcularAsync_ShouldLoadFeriadosAndCalculate()
    {
        var feriados = new[] { new DateOnly(2024, 7, 17) };
        _readRepo.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(feriados));

        var result = await _service.CalcularDiasUteisAsync(
            new DateOnly(2024, 7, 15),
            new DateOnly(2024, 7, 19),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task CalcularAsync_WhenRepoFails_ShouldReturnFailure()
    {
        _readRepo.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Failure(
                new Error("Repository.Error", "Database error")));

        var result = await _service.CalcularDiasUteisAsync(
            new DateOnly(2024, 7, 15),
            new DateOnly(2024, 7, 19),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task CalcularAsync_WithNoFeriados_ShouldCountAllWeekdays()
    {
        _readRepo.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(
                Array.Empty<DateOnly>()));

        var result = await _service.CalcularDiasUteisAsync(
            new DateOnly(2024, 7, 15),
            new DateOnly(2024, 7, 19),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(4);
    }
}
