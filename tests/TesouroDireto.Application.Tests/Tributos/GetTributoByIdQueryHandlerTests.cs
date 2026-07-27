using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class GetTributoByIdQueryHandlerTests
{
    private readonly ITributoReadRepository _repository = Substitute.For<ITributoReadRepository>();
    private readonly GetTributoByIdQueryHandler _handler;

    public GetTributoByIdQueryHandlerTests()
    {
        _handler = new GetTributoByIdQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithKnownId_ShouldReturnMappedDto()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        _repository.GetByIdAsync(tributo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        var result = await _handler.Handle(new GetTributoByIdQuery(tributo.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(tributo.Id);
        result.Value.Nome.Should().Be("IR");
        result.Value.BaseCalculo.Should().Be("Rendimento");
        result.Value.TipoCalculo.Should().Be("FaixaPorDias");
        result.Value.Faixas.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithUnknownId_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();

        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Failure(TributoErrors.NotFound));

        var result = await _handler.Handle(new GetTributoByIdQuery(id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.NotFound");
    }
}
