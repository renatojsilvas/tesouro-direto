using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecoAtualByNomeQueryHandlerTests
{
    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecoAtualByNomeQueryHandler _handler;

    public GetPrecoAtualByNomeQueryHandlerTests()
    {
        _handler = new GetPrecoAtualByNomeQueryHandler(_tituloReadRepo, _precoReadRepo);
    }

    [Fact]
    public async Task Handle_WithValidNome_ShouldReturnPrecoAtual()
    {
        var tituloId = Guid.NewGuid();
        var titulo = new TituloDto(tituloId, "Tesouro IPCA+", "2035-05-15", "IPCA", false, false);
        var preco = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 6.50m, 6.55m, 3200.00m, 3198.00m, 3197.50m);

        _tituloReadRepo.GetByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>())
            .Returns(Result<TituloDto>.Success(titulo));
        _precoReadRepo.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco));

        var result = await _handler.Handle(
            new GetPrecoAtualByNomeQuery("Tesouro IPCA+ 2035"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(preco);
    }

    [Fact]
    public async Task Handle_WithUnknownNome_ShouldReturnNotFound()
    {
        _tituloReadRepo.GetByNomeAsync("Tesouro Inexistente 2099", Arg.Any<CancellationToken>())
            .Returns(Result<TituloDto>.Failure(new Error("Titulo.NotFound", "not found")));

        var result = await _handler.Handle(
            new GetPrecoAtualByNomeQuery("Tesouro Inexistente 2099"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithEmptyNome_ShouldReturnInvalidNome()
    {
        var result = await _handler.Handle(
            new GetPrecoAtualByNomeQuery(""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidNome");
    }
}
