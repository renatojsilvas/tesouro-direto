using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Tests.Usuarios;

public sealed class DesativarUsuarioCommandHandlerTests
{
    private readonly IUsuarioWriteRepository _writeRepo = Substitute.For<IUsuarioWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly DesativarUsuarioCommandHandler _handler;

    public DesativarUsuarioCommandHandlerTests()
    {
        _handler = new DesativarUsuarioCommandHandler(_writeRepo, _unitOfWork);
    }

    [Fact]
    public async Task Handle_WhenSubUnknown_ShouldReturnNotFound()
    {
        _writeRepo.GetByGoogleSubAsync("sub-desconhecido", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));

        var result = await _handler.Handle(new DesativarUsuarioCommand("sub-desconhecido"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.NotFound");
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSubExists_ShouldDeactivateAndSave()
    {
        var usuario = Usuario.Create(
            Email.Create("ativo@exemplo.com").Value, "Ativo", PapelUsuario.User,
            new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero), "sub-ativo").Value;

        _writeRepo.GetByGoogleSubAsync("sub-ativo", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(usuario));
        _writeRepo.UpdateAsync(usuario, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _handler.Handle(new DesativarUsuarioCommand("sub-ativo"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        usuario.Ativo.Should().BeFalse();
        await _writeRepo.Received(1).UpdateAsync(usuario, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
