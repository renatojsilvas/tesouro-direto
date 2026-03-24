using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecosByNomeQueryHandlerTests
{
    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecosByNomeQueryHandler _handler;

    public GetPrecosByNomeQueryHandlerTests()
    {
        _handler = new GetPrecosByNomeQueryHandler(_tituloReadRepo, _precoReadRepo);
    }

    [Fact]
    public async Task Handle_WithValidNome_ShouldReturnPrecos()
    {
        var tituloId = Guid.NewGuid();
        var titulo = new TituloDto(tituloId, "Tesouro Selic", "2029-03-01", "Selic", false, false);
        var precos = new List<PrecoTaxaDto>
        {
            new(Guid.NewGuid(), "2025-03-23", 0.10m, 0.04m, 15800.00m, 15790.00m, 15785.00m),
            new(Guid.NewGuid(), "2025-03-24", 0.10m, 0.04m, 15810.00m, 15800.00m, 15795.00m)
        };

        _tituloReadRepo.GetByNomeAsync("Tesouro Selic 2029", Arg.Any<CancellationToken>())
            .Returns(Result<TituloDto>.Success(titulo));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByNomeQuery("Tesouro Selic 2029", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithDateFilters_ShouldPassThrough()
    {
        var tituloId = Guid.NewGuid();
        var titulo = new TituloDto(tituloId, "Tesouro Selic", "2029-03-01", "Selic", false, false);
        var dataInicio = new DateOnly(2025, 1, 1);
        var dataFim = new DateOnly(2025, 3, 24);

        _tituloReadRepo.GetByNomeAsync("Tesouro Selic 2029", Arg.Any<CancellationToken>())
            .Returns(Result<TituloDto>.Success(titulo));
        _precoReadRepo.GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(new List<PrecoTaxaDto>()));

        var result = await _handler.Handle(
            new GetPrecosByNomeQuery("Tesouro Selic 2029", dataInicio, dataFim), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _precoReadRepo.Received(1).GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownNome_ShouldReturnNotFound()
    {
        _tituloReadRepo.GetByNomeAsync("Tesouro Inexistente 2099", Arg.Any<CancellationToken>())
            .Returns(Result<TituloDto>.Failure(new Error("Titulo.NotFound", "not found")));

        var result = await _handler.Handle(
            new GetPrecosByNomeQuery("Tesouro Inexistente 2099", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithEmptyNome_ShouldReturnInvalidNome()
    {
        var result = await _handler.Handle(
            new GetPrecosByNomeQuery("  ", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidNome");
    }
}
