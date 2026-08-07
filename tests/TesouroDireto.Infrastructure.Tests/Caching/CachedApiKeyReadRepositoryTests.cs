using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedApiKeyReadRepositoryTests : IDisposable
{
    private readonly IApiKeyReadRepository _inner = Substitute.For<IApiKeyReadRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly CachedApiKeyReadRepository _sut;

    public CachedApiKeyReadRepositoryTests()
    {
        _sut = new CachedApiKeyReadRepository(_inner, _cache, _invalidator, new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task GetActiveByHashAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var dto = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        var result = await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _inner.Received(1).GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveByHashAsync_CacheHit_ShouldNotCallInner()
    {
        var dto = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);
        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        await _inner.Received(1).GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveByHashAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var dto = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);
        _invalidator.InvalidateApiKeys();
        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        await _inner.Received(2).GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveByHashAsync_FailureResult_ShouldNotCache()
    {
        var error = new Error("ApiKey.NotFound", "not found");
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Failure(error));

        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);
        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        await _inner.Received(2).GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveByHashAsync_DifferentHashes_ShouldCacheSeparately()
    {
        var dto1 = CreateDto();
        var dto2 = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto1));
        _inner.GetActiveByHashAsync("hash-2", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto2));

        await _sut.GetActiveByHashAsync("hash-1", CancellationToken.None);
        await _sut.GetActiveByHashAsync("hash-2", CancellationToken.None);

        await _inner.Received(1).GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>());
        await _inner.Received(1).GetActiveByHashAsync("hash-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveByHashAsync_NoConfig_ShouldUseDefaultTtlOfFiveMinutes()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var sut = new CachedApiKeyReadRepository(_inner, cache, _invalidator, new ConfigurationBuilder().Build());
        var dto = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        await sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GetActiveByHashAsync_ConfigPresent_ShouldOverrideDefault()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:ApiKeys"] = "00:10:00" })
            .Build();
        var sut = new CachedApiKeyReadRepository(_inner, cache, _invalidator, config);
        var dto = CreateDto();
        _inner.GetActiveByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        await sut.GetActiveByHashAsync("hash-1", CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetByHashAsync_ShouldAlwaysDelegateToInnerWithoutCaching()
    {
        var dto = CreateDto();
        _inner.GetByHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(Result<ApiKeyDto>.Success(dto));

        await _sut.GetByHashAsync("hash-1", CancellationToken.None);
        await _sut.GetByHashAsync("hash-1", CancellationToken.None);

        await _inner.Received(2).GetByHashAsync("hash-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByDonoAsync_ShouldAlwaysDelegateToInnerWithoutCaching()
    {
        var donoId = Guid.NewGuid();
        _inner.ListByDonoAsync(donoId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<ApiKeyDto>>.Success(Array.Empty<ApiKeyDto>()));

        await _sut.ListByDonoAsync(donoId, CancellationToken.None);
        await _sut.ListByDonoAsync(donoId, CancellationToken.None);

        await _inner.Received(2).ListByDonoAsync(donoId, Arg.Any<CancellationToken>());
    }

    private static ApiKeyDto CreateDto() => new(
        Guid.NewGuid(),
        "Sistema Parceiro",
        "abcd1234",
        Guid.NewGuid(),
        true,
        new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
        null);

    public void Dispose() => _cache.Dispose();
}
