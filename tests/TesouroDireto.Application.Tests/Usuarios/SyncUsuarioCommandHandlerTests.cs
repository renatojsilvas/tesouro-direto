using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Tests.Usuarios;

public sealed class SyncUsuarioCommandHandlerTests
{
    private readonly IUsuarioWriteRepository _writeRepo = Substitute.For<IUsuarioWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly SyncUsuarioCommandHandler _handler;

    public SyncUsuarioCommandHandlerTests()
    {
        _handler = new SyncUsuarioCommandHandler(_writeRepo, _unitOfWork, _timeProvider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenGoogleSubIsMissing_ShouldReturnFailureWithoutAnyLookup(string? googleSub)
    {
        var command = new SyncUsuarioCommand(googleSub!, "alguem@exemplo.com", "Fulano", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.GoogleSubObrigatorio");
        await _writeRepo.DidNotReceive().GetByGoogleSubAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _writeRepo.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
        await _writeRepo.DidNotReceive().AddOrGetExistingAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGoogleSubIsMissing_ShouldNeverMatchUsuarioWithNullGoogleSub()
    {
        var admin = Usuario.Create(
            Email.Create("admin-seed@exemplo.com").Value, "Admin", PapelUsuario.Admin,
            _timeProvider.GetUtcNow()).Value;
        admin.Aprovar(admin.Id, _timeProvider.GetUtcNow());

        _writeRepo.GetByGoogleSubAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(admin));

        var command = new SyncUsuarioCommand(null!, "attacker@evil.com", "X", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.GoogleSubObrigatorio");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenNomeIsEmpty_ShouldReturnFailure(string nome)
    {
        var command = new SyncUsuarioCommand("sub-1", "alguem@exemplo.com", nome, true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.InvalidNome");
        await _writeRepo.DidNotReceive().GetByGoogleSubAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailNotVerified_ShouldReturnFailure()
    {
        var command = new SyncUsuarioCommand("sub-1", "novo@exemplo.com", "Fulano", false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.EmailNaoVerificado");
        await _writeRepo.DidNotReceive().AddOrGetExistingAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGoogleSubAlreadyLinked_ShouldReturnExistingWithoutWriting()
    {
        var usuario = Usuario.Create(
            Email.Create("existente@exemplo.com").Value, "Existente", PapelUsuario.User,
            _timeProvider.GetUtcNow(), "sub-existente").Value;

        _writeRepo.GetByGoogleSubAsync("sub-existente", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(usuario));

        var command = new SyncUsuarioCommand("sub-existente", "existente@exemplo.com", "Existente", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(usuario.Id);
        result.Value.Aprovado.Should().BeFalse();
        await _writeRepo.DidNotReceive().AddOrGetExistingAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSeedAdminMatchesByEmail_ShouldLinkGoogleSubAndKeepAdminAprovado()
    {
        var admin = Usuario.Create(
            Email.Create("admin@tesouro.test").Value, "Admin", PapelUsuario.Admin,
            _timeProvider.GetUtcNow()).Value;
        admin.Aprovar(Guid.NewGuid(), _timeProvider.GetUtcNow());

        _writeRepo.GetByGoogleSubAsync("sub-admin", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(admin));
        _writeRepo.UpdateAsync(admin, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var command = new SyncUsuarioCommand("sub-admin", "admin@tesouro.test", "Admin", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(admin.Id);
        result.Value.Aprovado.Should().BeTrue();
        admin.GoogleSub.Should().Be("sub-admin");
        admin.Papel.Should().Be(PapelUsuario.Admin);
        await _writeRepo.Received(1).UpdateAsync(admin, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoMatchFound_ShouldCreateNewUnapprovedUsuario()
    {
        _writeRepo.GetByGoogleSubAsync("sub-novo", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.AddOrGetExistingAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<Usuario>.Success(callInfo.Arg<Usuario>()));

        var command = new SyncUsuarioCommand("sub-novo", "novo@exemplo.com", "Fulano", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Aprovado.Should().BeFalse();
        await _writeRepo.Received(1).AddOrGetExistingAsync(
            Arg.Is<Usuario>(u => u.GoogleSub == "sub-novo" && u.Papel == PapelUsuario.User),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAddOrGetExistingRacesWithConcurrentInsert_ShouldReturnResolvedExistingUsuario()
    {
        var concorrente = Usuario.Create(
            Email.Create("concorrente@exemplo.com").Value, "Concorrente", PapelUsuario.User,
            _timeProvider.GetUtcNow(), "sub-concorrente").Value;

        _writeRepo.GetByGoogleSubAsync("sub-concorrente", Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.AddOrGetExistingAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(concorrente));

        var command = new SyncUsuarioCommand("sub-concorrente", "concorrente@exemplo.com", "Concorrente", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(concorrente.Id);
    }

    [Fact]
    public async Task Handle_WithInvalidEmail_ShouldReturnFailure()
    {
        _writeRepo.GetByGoogleSubAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));

        var command = new SyncUsuarioCommand("sub-invalido", "sem-arroba", "Fulano", true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Email.Invalid");
    }
}
