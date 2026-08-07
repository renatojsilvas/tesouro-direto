using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.ApiKeys;

public sealed class GetMinhasKeysQueryHandlerTests
{
    private readonly IApiKeyReadRepository _readRepo = Substitute.For<IApiKeyReadRepository>();
    private readonly GetMinhasKeysQueryHandler _handler;

    public GetMinhasKeysQueryHandlerTests()
    {
        _handler = new GetMinhasKeysQueryHandler(_readRepo);
    }

    [Fact]
    public async Task Handle_ShouldReturnOnlyKeysOfDonoFromRepository()
    {
        var donoId = Guid.NewGuid();
        IReadOnlyCollection<ApiKeyDto> minhasKeys =
        [
            new ApiKeyDto(Guid.NewGuid(), "Sistema A", "td_aaaaaa", donoId, true, DateTimeOffset.UtcNow, null),
            new ApiKeyDto(Guid.NewGuid(), "Sistema B", "td_bbbbbb", donoId, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        _readRepo.ListByDonoAsync(donoId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<ApiKeyDto>>.Success(minhasKeys));

        var result = await _handler.Handle(new GetMinhasKeysQuery(donoId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(minhasKeys);
        await _readRepo.Received(1).ListByDonoAsync(donoId, Arg.Any<CancellationToken>());
    }
}
