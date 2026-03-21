using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Titulos;

namespace TesouroDireto.Application.Tests.Titulos;

public sealed class GetTitulosQueryHandlerTests
{
    private readonly ITituloReadRepository _repository = Substitute.For<ITituloReadRepository>();
    private readonly GetTitulosQueryHandler _handler;

    public GetTitulosQueryHandlerTests()
    {
        _handler = new GetTitulosQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithNoFilters_ShouldReturnAllTitulos()
    {
        var titulos = new[]
        {
            new TituloDto(Guid.NewGuid(), "Tesouro Selic", "2029-03-01", "Selic", false, false),
            new TituloDto(Guid.NewGuid(), "Tesouro IPCA+", "2035-05-15", "IPCA", false, false)
        };

        _repository
            .GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(titulos);

        var result = await _handler.Handle(new GetTitulosQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithIndexadorFilter_ShouldPassFilterToRepository()
    {
        var titulos = new[]
        {
            new TituloDto(Guid.NewGuid(), "Tesouro Selic", "2029-03-01", "Selic", false, false)
        };

        _repository
            .GetFilteredAsync("Selic", null, Arg.Any<CancellationToken>())
            .Returns(titulos);

        var result = await _handler.Handle(new GetTitulosQuery("Selic", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Indexador.Should().Be("Selic");
    }

    [Fact]
    public async Task Handle_WithVencidoFilter_ShouldPassFilterToRepository()
    {
        var titulos = new[]
        {
            new TituloDto(Guid.NewGuid(), "Tesouro Prefixado", "2020-01-01", "Prefixado", false, true)
        };

        _repository
            .GetFilteredAsync(null, true, Arg.Any<CancellationToken>())
            .Returns(titulos);

        var result = await _handler.Handle(new GetTitulosQuery(null, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Vencido.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidIndexador_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new GetTitulosQuery("InvalidIndexador", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Indexador.Invalid");
    }

    [Fact]
    public async Task Handle_WithBothFilters_ShouldPassBothToRepository()
    {
        var titulos = Array.Empty<TituloDto>();

        _repository
            .GetFilteredAsync("IPCA", false, Arg.Any<CancellationToken>())
            .Returns(titulos);

        var result = await _handler.Handle(new GetTitulosQuery("IPCA", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNoResults_ShouldReturnEmptyCollection()
    {
        _repository
            .GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TituloDto>());

        var result = await _handler.Handle(new GetTitulosQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
