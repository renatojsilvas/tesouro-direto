using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Tests.Usuarios;

public sealed class AprovarUsuarioCommandHandlerTests
{
    private readonly IUsuarioWriteRepository _writeRepo = Substitute.For<IUsuarioWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly AprovarUsuarioCommandHandler _handler;

    public AprovarUsuarioCommandHandlerTests()
    {
        _handler = new AprovarUsuarioCommandHandler(_writeRepo, _unitOfWork, _timeProvider);
    }

    [Fact]
    public async Task Handle_WhenSubUnknown_ShouldReturnNotFound()
    {
        _writeRepo.GetByGoogleSubAsync("sub-desconhecido", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));

        var result = await _handler.Handle(new AprovarUsuarioCommand("sub-desconhecido", Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.NotFound");
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSubExists_ShouldApproveAndSave()
    {
        var usuario = Usuario.Create(
            Email.Create("pendente@exemplo.com").Value, "Pendente", PapelUsuario.User,
            _timeProvider.GetUtcNow(), "sub-pendente").Value;
        var adminId = Guid.NewGuid();

        _writeRepo.GetByGoogleSubAsync("sub-pendente", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(usuario));
        _writeRepo.UpdateAsync(usuario, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _handler.Handle(new AprovarUsuarioCommand("sub-pendente", adminId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        usuario.Aprovado.Should().BeTrue();
        usuario.AprovadoPor.Should().Be(adminId);
        await _writeRepo.Received(1).UpdateAsync(usuario, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
