using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class UpdateTributoCommandHandlerTests
{
    private readonly ITributoReadRepository _readRepo = Substitute.For<ITributoReadRepository>();
    private readonly ITributoWriteRepository _writeRepo = Substitute.For<ITributoWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UpdateTributoCommandHandler _handler;

    public UpdateTributoCommandHandlerTests()
    {
        _handler = new UpdateTributoCommandHandler(_readRepo, _writeRepo, _unitOfWork, Substitute.For<ICacheInvalidator>());

        _writeRepo.UpdateAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateAndSave()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        _readRepo.GetByIdAsync(tributo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        var newFaixas = new[] { new FaixaDto(0, 360, null, 20m) };
        var command = new UpdateTributoCommand(tributo.Id, false, newFaixas);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tributo.Ativo.Should().BeFalse();
        tributo.Faixas.Should().HaveCount(1);
        tributo.Faixas.First().Aliquota.Should().Be(20m);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNonExistentId_ShouldReturnNotFound()
    {
        _readRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Failure(TributoErrors.NotFound));

        var command = new UpdateTributoCommand(Guid.NewGuid(), true, new[] { new FaixaDto(0, 180, null, 22.5m) });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.NotFound");
    }

    [Fact]
    public async Task Handle_WithInvalidFaixa_ShouldReturnFailure()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        _readRepo.GetByIdAsync(tributo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        var invalidFaixas = new[] { new FaixaDto(null, null, null, 22.5m) };
        var command = new UpdateTributoCommand(tributo.Id, true, invalidFaixas);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Faixa.NoCriteria");
    }

    [Fact]
    public async Task Handle_WithEmptyFaixas_ShouldReturnFailure()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        _readRepo.GetByIdAsync(tributo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        var command = new UpdateTributoCommand(tributo.Id, true, Array.Empty<FaixaDto>());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.NoFaixas");
    }

    [Fact]
    public async Task Handle_ShouldActivateTributo()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;
        tributo.Desativar();

        _readRepo.GetByIdAsync(tributo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        var command = new UpdateTributoCommand(tributo.Id, true, new[] { new FaixaDto(0, 180, null, 22.5m) });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tributo.Ativo.Should().BeTrue();
    }
}
