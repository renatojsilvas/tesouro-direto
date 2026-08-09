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

    public void Dispose() => _cache.Dispose();
}
