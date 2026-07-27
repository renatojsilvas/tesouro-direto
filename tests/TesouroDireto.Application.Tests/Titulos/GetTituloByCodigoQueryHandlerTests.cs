using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.Titulos;

public sealed class GetTituloByCodigoQueryHandlerTests
{
    private readonly ITituloReadRepository _repository = Substitute.For<ITituloReadRepository>();
    private readonly GetTituloByCodigoQueryHandler _handler;

    public GetTituloByCodigoQueryHandlerTests()
    {
        _handler = new GetTituloByCodigoQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithKnownCodigo_ShouldReturnMatchingTitulo()
    {
        IReadOnlyCollection<TituloDto> titulos = new[]
        {
            new TituloDto("Tesouro Selic", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01"),
            new TituloDto("Tesouro IPCA+", "2035-05-15", "IPCA", false, false, "tesouro-ipca-mais-2035-05-15")
        };

        _repository
            .GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        var result = await _handler.Handle(
            new GetTituloByCodigoQuery("tesouro-ipca-mais-2035-05-15"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Codigo.Should().Be("tesouro-ipca-mais-2035-05-15");
        result.Value.TipoTitulo.Should().Be("Tesouro IPCA+");
    }

    [Fact]
    public async Task Handle_WithMalformedCodigo_ShouldReturnInvalidCodigoWithoutQueryingRepository()
    {
        var result = await _handler.Handle(
            new GetTituloByCodigoQuery("nao-e-um-codigo-valido"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidCodigo");
        await _repository.DidNotReceive().GetFilteredAsync(
            Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownWellFormedCodigo_ShouldReturnNotFound()
    {
        IReadOnlyCollection<TituloDto> titulos = new[]
        {
            new TituloDto("Tesouro Selic", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
        };

        _repository
            .GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        var result = await _handler.Handle(
            new GetTituloByCodigoQuery("tesouro-selic-2099-01-01"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }
}
