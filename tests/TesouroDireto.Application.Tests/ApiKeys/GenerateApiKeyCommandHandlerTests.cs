using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.ApiKeys;

public sealed class GenerateApiKeyCommandHandlerTests
{
    private readonly IApiKeyGenerator _generator = Substitute.For<IApiKeyGenerator>();
    private readonly IApiKeyWriteRepository _writeRepo = Substitute.For<IApiKeyWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly GenerateApiKeyCommandHandler _handler;

    public GenerateApiKeyCommandHandlerTests()
    {
        _handler = new GenerateApiKeyCommandHandler(_generator, _writeRepo, _unitOfWork, _timeProvider);
    }

    [Fact]
    public async Task Handle_WithValidNome_ShouldReturnRawKeyAndPersistHash()
    {
        var donoId = Guid.NewGuid();
        _generator.Generate().Returns(new GeneratedKeyMaterial("td_rawkeytexto", "td_rawke"));
        _writeRepo.AddAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var result = await _handler.Handle(new GenerateApiKeyCommand(donoId, "Sistema Parceiro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Chave.Should().Be("td_rawkeytexto");
        result.Value.Nome.Should().Be("Sistema Parceiro");
        result.Value.Prefixo.Should().Be("td_rawke");
        result.Value.CriadaEm.Should().Be(_timeProvider.GetUtcNow());

        await _writeRepo.Received(1).AddAsync(
            Arg.Is<ApiKey>(k =>
                k.DonoUsuarioId == donoId
                && k.Hash == ApiKeyHash.FromRawKey("td_rawkeytexto")
                && k.Prefixo == "td_rawke"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithEmptyNome_ShouldReturnInvalidNomeAndNotPersist()
    {
        _generator.Generate().Returns(new GeneratedKeyMaterial("td_rawkeytexto", "td_rawke"));

        var result = await _handler.Handle(new GenerateApiKeyCommand(Guid.NewGuid(), "   "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.InvalidNome");
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAddAsyncFails_ShouldReturnFailureAndNotSave()
    {
        _generator.Generate().Returns(new GeneratedKeyMaterial("td_rawkeytexto", "td_rawke"));
        _writeRepo.AddAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ApiKeyErrors.AlreadyExists));

        var result = await _handler.Handle(new GenerateApiKeyCommand(Guid.NewGuid(), "Sistema"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.AlreadyExists");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
