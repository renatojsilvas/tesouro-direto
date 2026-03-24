using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedTituloReadRepositoryTests : IDisposable
{
    private readonly ITituloReadRepository _inner = Substitute.For<ITituloReadRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly CachedTituloReadRepository _sut;

    public CachedTituloReadRepositoryTests()
    {
        _sut = new CachedTituloReadRepository(_inner, _cache, _invalidator);
    }

    [Fact]
    public async Task GetFilteredAsync_CacheMiss_ShouldCallInnerAndCacheResult()
    {
        var titulos = new List<TituloDto>
        {
            new(Guid.NewGuid(), "Tesouro Selic 2029", "2029-03-01", "Selic", false, false)
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        var result = await _sut.GetFilteredAsync(null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        await _inner.Received(1).GetFilteredAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilteredAsync_CacheHit_ShouldNotCallInner()
    {
        var titulos = new List<TituloDto>
        {
            new(Guid.NewGuid(), "Tesouro Selic 2029", "2029-03-01", "Selic", false, false)
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        await _sut.GetFilteredAsync(null, null, CancellationToken.None);
        var result = await _sut.GetFilteredAsync(null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _inner.Received(1).GetFilteredAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilteredAsync_DifferentFilters_ShouldUseDifferentCacheKeys()
    {
        var selicTitulos = new List<TituloDto>
        {
            new(Guid.NewGuid(), "Tesouro Selic 2029", "2029-03-01", "Selic", false, false)
        };
        var ipcaTitulos = new List<TituloDto>
        {
            new(Guid.NewGuid(), "Tesouro IPCA+ 2035", "2035-05-15", "IPCA", false, false)
        };

        _inner.GetFilteredAsync("Selic", null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(selicTitulos));
        _inner.GetFilteredAsync("IPCA", null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(ipcaTitulos));

        var result1 = await _sut.GetFilteredAsync("Selic", null, CancellationToken.None);
        var result2 = await _sut.GetFilteredAsync("IPCA", null, CancellationToken.None);

        result1.Value.First().Indexador.Should().Be("Selic");
        result2.Value.First().Indexador.Should().Be("IPCA");
        await _inner.Received(1).GetFilteredAsync("Selic", null, Arg.Any<CancellationToken>());
        await _inner.Received(1).GetFilteredAsync("IPCA", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilteredAsync_FailureResult_ShouldNotCache()
    {
        var error = new Error("Test.Error", "test");
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Failure(error));

        await _sut.GetFilteredAsync(null, null, CancellationToken.None);
        await _sut.GetFilteredAsync(null, null, CancellationToken.None);

        await _inner.Received(2).GetFilteredAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilteredAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var titulos = new List<TituloDto>
        {
            new(Guid.NewGuid(), "Tesouro Selic 2029", "2029-03-01", "Selic", false, false)
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        await _sut.GetFilteredAsync(null, null, CancellationToken.None);
        _invalidator.InvalidateTitulos();
        await _sut.GetFilteredAsync(null, null, CancellationToken.None);

        await _inner.Received(2).GetFilteredAsync(null, null, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _cache.Dispose();
}
