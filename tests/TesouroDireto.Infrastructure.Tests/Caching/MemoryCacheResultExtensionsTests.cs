using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class MemoryCacheResultExtensionsTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10 });

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetOrCreateResultAsync_WithSizeLimitedCache_ShouldNotThrow_BecauseEntrySpecifiesSize()
    {
        var act = async () => await _cache.GetOrCreateResultAsync(
            "probe-key",
            TimeSpan.FromMinutes(1),
            CancellationToken.None,
            () => Task.FromResult(Result<string>.Success("valor")));

        await act.Should().NotThrowAsync<InvalidOperationException>(
            "o helper compartilhado por 5 dos 6 decorators de cache precisa declarar Size " +
            "em toda entrada quando o IMemoryCache tem SizeLimit configurado — sem isso o " +
            "MemoryCache lança InvalidOperationException em runtime no primeiro cache miss");
    }

    [Fact]
    public async Task GetOrCreateResultAsync_WithSizeLimitedCache_SuccessfulEntry_ShouldActuallyBeCached()
    {
        var callCount = 0;

        Task<Result<string>> Factory()
        {
            callCount++;
            return Task.FromResult(Result<string>.Success("valor"));
        }

        await _cache.GetOrCreateResultAsync("probe-key", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);
        await _cache.GetOrCreateResultAsync("probe-key", TimeSpan.FromMinutes(1), CancellationToken.None, Factory);

        callCount.Should().Be(1, "a segunda chamada deveria ser atendida pelo cache, não pela factory");
    }
}
