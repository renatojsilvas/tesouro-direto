using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecoAtualByCodigoQueryHandlerTests
{
    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecoAtualByCodigoQueryHandler _handler;

    public GetPrecoAtualByCodigoQueryHandlerTests()
    {
        _handler = new GetPrecoAtualByCodigoQueryHandler(_tituloReadRepo, _precoReadRepo);
    }

    [Fact]
    public async Task Handle_WithValidCodigo_ShouldReturnPrecoAtual()
    {
        var tituloId = Guid.NewGuid();
        var preco = new PrecoTaxaDto("2025-03-24", 6.50m, 6.55m, 3200.00m, 3198.00m, 3197.50m);

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco));

        var result = await _handler.Handle(
            new GetPrecoAtualByCodigoQuery("tesouro-ipca-mais-2035-05-15"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(preco);
    }

    [Fact]
    public async Task Handle_WithUnknownCodigo_ShouldReturnNotFound()
    {
        _tituloReadRepo.GetIdByCodigoAsync("tesouro-inexistente-2099-01-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.NotFound", "not found")));

        var result = await _handler.Handle(
            new GetPrecoAtualByCodigoQuery("tesouro-inexistente-2099-01-01"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithMalformedCodigo_ShouldReturnInvalidCodigo()
    {
        _tituloReadRepo.GetIdByCodigoAsync("nao-e-um-codigo-valido", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.InvalidCodigo", "malformed")));

        var result = await _handler.Handle(
            new GetPrecoAtualByCodigoQuery("nao-e-um-codigo-valido"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidCodigo");
    }
}
