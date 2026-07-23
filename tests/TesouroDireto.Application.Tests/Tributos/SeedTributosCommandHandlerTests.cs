using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;
using Xunit;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class SeedTributosCommandHandlerTests
{
    private readonly ITributoReadRepository _readRepo = Substitute.For<ITributoReadRepository>();
    private readonly ITributoWriteRepository _writeRepo = Substitute.For<ITributoWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SeedTributosCommandHandler _handler;

    public SeedTributosCommandHandlerTests()
    {
        _handler = new SeedTributosCommandHandler(_readRepo, _writeRepo, _unitOfWork);
        _writeRepo.AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WhenEmpty_ShouldSeedIofAndIrAndSave()
    {
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success([]));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.Received(2).AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAlreadySeeded_ShouldBeNoOp()
    {
        var existente = TributosPadrao.Build().Value;
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(existente.ToList()));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenReadFails_ShouldReturnFailure()
    {
        var error = new Error("Tributo.ReadFailed", "boom");
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Failure(error));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
    }
}
