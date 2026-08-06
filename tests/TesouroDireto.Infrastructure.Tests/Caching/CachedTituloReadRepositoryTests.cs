using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
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
        _sut = new CachedTituloReadRepository(_inner, _cache, _invalidator, new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task GetFilteredAsync_CacheMiss_ShouldCallInnerAndCacheResult()
    {
        var titulos = new List<TituloDto>
        {
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
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
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
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
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
        };
        var ipcaTitulos = new List<TituloDto>
        {
            new("Tesouro IPCA+ 2035", "2035-05-15", "IPCA", false, false, "tesouro-ipca-mais-2035-05-15")
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
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        await _sut.GetFilteredAsync(null, null, CancellationToken.None);
        _invalidator.InvalidateTitulos();
        await _sut.GetFilteredAsync(null, null, CancellationToken.None);

        await _inner.Received(2).GetFilteredAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByNomeAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        var result = await _sut.GetIdByNomeAsync("Tesouro IPCA+ 2035", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(tituloId);
        await _inner.Received(1).GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByNomeAsync_CacheHit_ShouldNotCallInner()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        await _sut.GetIdByNomeAsync("Tesouro IPCA+ 2035", CancellationToken.None);
        await _sut.GetIdByNomeAsync("Tesouro IPCA+ 2035", CancellationToken.None);

        await _inner.Received(1).GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByNomeAsync_CaseInsensitive_ShouldUseSameCacheKey()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByNomeAsync("tesouro ipca+ 2035", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        await _sut.GetIdByNomeAsync("tesouro ipca+ 2035", CancellationToken.None);
        await _sut.GetIdByNomeAsync("TESOURO IPCA+ 2035", CancellationToken.None);

        await _inner.Received(1).GetIdByNomeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByNomeAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        await _sut.GetIdByNomeAsync("Tesouro IPCA+ 2035", CancellationToken.None);
        _invalidator.InvalidateTitulos();
        await _sut.GetIdByNomeAsync("Tesouro IPCA+ 2035", CancellationToken.None);

        await _inner.Received(2).GetIdByNomeAsync("Tesouro IPCA+ 2035", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByCodigoAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        var result = await _sut.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(tituloId);
        await _inner.Received(1).GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByCodigoAsync_CacheHit_ShouldNotCallInner()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        await _sut.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", CancellationToken.None);
        await _sut.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", CancellationToken.None);

        await _inner.Received(1).GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdByCodigoAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var tituloId = Guid.NewGuid();
        _inner.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));

        await _sut.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", CancellationToken.None);
        _invalidator.InvalidateTitulos();
        await _sut.GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", CancellationToken.None);

        await _inner.Received(2).GetIdByCodigoAsync("tesouro-ipca-mais-2035-05-15", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilteredAsync_NoConfig_ShouldUseDefaultTtlOfTwentyFourHours()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var sut = new CachedTituloReadRepository(_inner, cache, _invalidator, new ConfigurationBuilder().Build());
        var titulos = new List<TituloDto>
        {
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        await sut.GetFilteredAsync(null, null, CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task GetFilteredAsync_ConfigPresent_ShouldOverrideDefault()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:Titulos"] = "10.00:00:00" })
            .Build();
        var sut = new CachedTituloReadRepository(_inner, cache, _invalidator, config);
        var titulos = new List<TituloDto>
        {
            new("Tesouro Selic 2029", "2029-03-01", "Selic", false, false, "tesouro-selic-2029-03-01")
        };
        _inner.GetFilteredAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<TituloDto>>.Success(titulos));

        await sut.GetFilteredAsync(null, null, CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromDays(10));
    }

    public void Dispose() => _cache.Dispose();
}
