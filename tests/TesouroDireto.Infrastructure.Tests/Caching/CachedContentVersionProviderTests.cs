using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.Http;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedContentVersionProviderTests : IDisposable
{
    private readonly IContentVersionProvider _inner = Substitute.For<IContentVersionProvider>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly CachedContentVersionProvider _sut;

    public CachedContentVersionProviderTests()
    {
        _sut = new CachedContentVersionProvider(_inner, _cache, new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task GetVersionAsync_CalledTwice_ShouldCallInnerOnce()
    {
        _inner.GetVersionAsync(Arg.Any<CancellationToken>()).Returns("2026-08-08-10-5");

        var first = await _sut.GetVersionAsync(CancellationToken.None);
        var second = await _sut.GetVersionAsync(CancellationToken.None);

        first.Should().Be(second);
        await _inner.Received(1).GetVersionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVersionAsync_CacheMiss_ShouldReturnVersionProducedByInner()
    {
        _inner.GetVersionAsync(Arg.Any<CancellationToken>()).Returns("2026-08-08-10-5");

        var version = await _sut.GetVersionAsync(CancellationToken.None);

        version.Should().Be("2026-08-08-10-5");
    }

    [Fact]
    public async Task GetVersionAsync_WithZeroTtl_ShouldBypassCacheAndCallInnerEachTime()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:ContentVersion"] = "00:00:00" })
            .Build();
        var sut = new CachedContentVersionProvider(_inner, _cache, config);
        _inner.GetVersionAsync(Arg.Any<CancellationToken>()).Returns("2026-08-08-10-5");

        await sut.GetVersionAsync(CancellationToken.None);
        await sut.GetVersionAsync(CancellationToken.None);

        await _inner.Received(2).GetVersionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVersionAsync_WithSizeLimitedCache_ShouldNotThrow_BecauseEntrySpecifiesSize()
    {
        using var sizeLimitedCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var sut = new CachedContentVersionProvider(_inner, sizeLimitedCache, new ConfigurationBuilder().Build());
        _inner.GetVersionAsync(Arg.Any<CancellationToken>()).Returns("2026-08-08-10-5");

        var act = async () => await sut.GetVersionAsync(CancellationToken.None);

        await act.Should().NotThrowAsync<InvalidOperationException>(
            "a entrada única deste provider precisa declarar Size quando o IMemoryCache " +
            "tem SizeLimit configurado — sem isso o MemoryCache lança InvalidOperationException " +
            "em runtime no primeiro cache miss");
    }

    public void Dispose() => _cache.Dispose();
}
