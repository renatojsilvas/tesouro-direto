using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;
using Xunit;

namespace TesouroDireto.Application.Tests.Usuarios;

public sealed class SeedAdminCommandHandlerTests
{
    private readonly IUsuarioWriteRepository _writeRepo = Substitute.For<IUsuarioWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private SeedAdminCommandHandler CreateHandler(IConfiguration configuration) =>
        new(_writeRepo, _unitOfWork, configuration, _timeProvider, NullLogger<SeedAdminCommandHandler>.Instance);

    private static IConfiguration BuildConfiguration(string? adminEmail) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(adminEmail is null
                ? []
                : new Dictionary<string, string?> { ["ADMIN_EMAIL"] = adminEmail })
            .Build();

    [Fact]
    public async Task Handle_WhenAdminEmailIsAbsent_ShouldBeNoOp()
    {
        var handler = CreateHandler(BuildConfiguration(null));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAdminEmailIsWhitespace_ShouldBeNoOp()
    {
        var handler = CreateHandler(BuildConfiguration("   "));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAdminEmailIsMalformed_ShouldReturnFailure()
    {
        var handler = CreateHandler(BuildConfiguration("sem-arroba"));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUsuarioDoesNotExist_ShouldCreateApprovedAdminAndSave()
    {
        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Failure(UsuarioErrors.NotFound));
        _writeRepo.AddAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = CreateHandler(BuildConfiguration("admin@tesouro.test"));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.Received(1).AddAsync(
            Arg.Is<Usuario>(u => u.Papel == PapelUsuario.Admin && u.Aprovado),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUsuarioExistsAsUnapprovedUser_ShouldPromoteAndSave()
    {
        var usuario = Usuario.Create(
            Email.Create("admin@tesouro.test").Value,
            "Admin",
            PapelUsuario.User,
            _timeProvider.GetUtcNow()).Value;

        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(usuario));
        _writeRepo.UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = CreateHandler(BuildConfiguration("admin@tesouro.test"));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        usuario.Papel.Should().Be(PapelUsuario.Admin);
        usuario.Aprovado.Should().BeTrue();
        await _writeRepo.Received(1).UpdateAsync(usuario, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUsuarioAlreadyApprovedAdmin_ShouldBeNoOp()
    {
        var usuario = Usuario.Create(
            Email.Create("admin@tesouro.test").Value,
            "Admin",
            PapelUsuario.Admin,
            _timeProvider.GetUtcNow()).Value;
        usuario.Aprovar(Guid.NewGuid(), _timeProvider.GetUtcNow());

        _writeRepo.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(Result<Usuario>.Success(usuario));
        var handler = CreateHandler(BuildConfiguration("admin@tesouro.test"));

        var result = await handler.Handle(new SeedAdminCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
