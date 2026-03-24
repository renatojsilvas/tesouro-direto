using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class CreateTributoCommandHandlerTests
{
    private readonly ITributoWriteRepository _writeRepo = Substitute.For<ITributoWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateTributoCommandHandler _handler;

    public CreateTributoCommandHandlerTests()
    {
        _handler = new CreateTributoCommandHandler(_writeRepo, _unitOfWork, Substitute.For<ICacheInvalidator>());

        _writeRepo.AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateAndReturnId()
    {
        var faixas = new[] { new FaixaDto(0, 180, null, 22.5m), new FaixaDto(181, 360, null, 20m) };
        var command = new CreateTributoCommand("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _writeRepo.Received(1).AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithEmptyNome_ShouldReturnFailure()
    {
        var faixas = new[] { new FaixaDto(0, 180, null, 22.5m) };
        var command = new CreateTributoCommand("", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.InvalidNome");
    }

    [Fact]
    public async Task Handle_WithEmptyFaixas_ShouldReturnFailure()
    {
        var command = new CreateTributoCommand("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, Array.Empty<FaixaDto>(), 1, false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.NoFaixas");
    }

    [Fact]
    public async Task Handle_WithInvalidFaixa_ShouldReturnFailure()
    {
        var faixas = new[] { new FaixaDto(null, null, null, 22.5m) };
        var command = new CreateTributoCommand("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Faixa.NoCriteria");
    }

    [Fact]
    public async Task Handle_WithNegativeOrdem_ShouldReturnFailure()
    {
        var faixas = new[] { new FaixaDto(0, 180, null, 22.5m) };
        var command = new CreateTributoCommand("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, -1, false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.InvalidOrdem");
    }
}
