using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.ApiKeys;

public sealed class RevokeApiKeyCommandHandlerTests
{
    private readonly IApiKeyWriteRepository _writeRepo = Substitute.For<IApiKeyWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly RevokeApiKeyCommandHandler _handler;

    public RevokeApiKeyCommandHandlerTests()
    {
        _handler = new RevokeApiKeyCommandHandler(_writeRepo, _unitOfWork, _timeProvider);
    }

    private static ApiKey NovaApiKey(Guid donoId) =>
        ApiKey.Create(
            "Sistema Parceiro",
            ApiKeyHash.FromRawKey("chave-de-teste"),
            "abcd1234",
            donoId,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

    [Fact]
    public async Task Handle_WhenKeyBelongsToDono_ShouldRevokeAndSave()
    {
        var donoId = Guid.NewGuid();
        var apiKey = NovaApiKey(donoId);

        _writeRepo.GetByIdAsync(apiKey.Id, Arg.Any<CancellationToken>()).Returns(Result<ApiKey>.Success(apiKey));
        _writeRepo.UpdateAsync(apiKey, Arg.Any<CancellationToken>()).Returns(Result.Success());

        var result = await _handler.Handle(new RevokeApiKeyCommand(apiKey.Id, donoId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        apiKey.Ativa.Should().BeFalse();
        apiKey.RevogadaEm.Should().Be(_timeProvider.GetUtcNow());
        await _writeRepo.Received(1).UpdateAsync(apiKey, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenKeyBelongsToAnotherDono_ShouldReturnNotFoundAndNotRevoke()
    {
        var donoId = Guid.NewGuid();
        var outroDonoId = Guid.NewGuid();
        var apiKey = NovaApiKey(outroDonoId);

        _writeRepo.GetByIdAsync(apiKey.Id, Arg.Any<CancellationToken>()).Returns(Result<ApiKey>.Success(apiKey));

        var result = await _handler.Handle(new RevokeApiKeyCommand(apiKey.Id, donoId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
        apiKey.Ativa.Should().BeTrue();
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenKeyDoesNotExist_ShouldReturnNotFound()
    {
        var keyId = Guid.NewGuid();
        _writeRepo.GetByIdAsync(keyId, Arg.Any<CancellationToken>())
            .Returns(Result<ApiKey>.Failure(ApiKeyErrors.NotFound));

        var result = await _handler.Handle(new RevokeApiKeyCommand(keyId, Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
        await _writeRepo.DidNotReceive().UpdateAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
    }
}
