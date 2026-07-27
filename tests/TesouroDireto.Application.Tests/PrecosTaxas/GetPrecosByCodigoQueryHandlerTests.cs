using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecosByCodigoQueryHandlerTests
{
    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecosByCodigoQueryHandler _handler;

    public GetPrecosByCodigoQueryHandlerTests()
    {
        _handler = new GetPrecosByCodigoQueryHandler(_tituloReadRepo, _precoReadRepo);
    }

    [Fact]
    public async Task Handle_WithValidCodigo_ShouldReturnPrecos()
    {
        var tituloId = Guid.NewGuid();
        var precos = new List<PrecoTaxaDto>
        {
            new("2025-03-23", 0.10m, 0.04m, 15800.00m, 15790.00m, 15785.00m),
            new("2025-03-24", 0.10m, 0.04m, 15810.00m, 15800.00m, 15795.00m)
        };

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithDateFilters_ShouldPassThrough()
    {
        var tituloId = Guid.NewGuid();
        var dataInicio = new DateOnly(2025, 1, 1);
        var dataFim = new DateOnly(2025, 3, 24);

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(new List<PrecoTaxaDto>()));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", dataInicio, dataFim), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _precoReadRepo.Received(1).GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownCodigo_ShouldReturnNotFound()
    {
        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2099-01-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.NotFound", "not found")));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2099-01-01", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithMalformedCodigo_ShouldReturnInvalidCodigo()
    {
        _tituloReadRepo.GetIdByCodigoAsync("nao-e-um-codigo-valido", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.InvalidCodigo", "malformed")));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("nao-e-um-codigo-valido", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidCodigo");
    }
}
