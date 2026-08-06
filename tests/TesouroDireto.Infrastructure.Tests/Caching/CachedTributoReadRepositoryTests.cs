using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedTributoReadRepositoryTests : IDisposable
{
    private readonly ITributoReadRepository _inner = Substitute.For<ITributoReadRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly CachedTributoReadRepository _sut;

    public CachedTributoReadRepositoryTests()
    {
        _sut = new CachedTributoReadRepository(_inner, _cache, _invalidator, new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task GetAllAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var tributos = CreateTributos();
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        var result = await _sut.GetAllAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _inner.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_CacheHit_ShouldNotCallInner()
    {
        var tributos = CreateTributos();
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        await _sut.GetAllAsync(CancellationToken.None);
        await _sut.GetAllAsync(CancellationToken.None);

        await _inner.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAtivosOrdenadosAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var tributos = CreateTributos();
        _inner.GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        var result = await _sut.GetAtivosOrdenadosAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _inner.Received(1).GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAtivosOrdenadosAsync_CacheHit_ShouldNotCallInner()
    {
        var tributos = CreateTributos();
        _inner.GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        await _sut.GetAtivosOrdenadosAsync(CancellationToken.None);
        await _sut.GetAtivosOrdenadosAsync(CancellationToken.None);

        await _inner.Received(1).GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_ShouldAlwaysCallInner()
    {
        var id = Guid.NewGuid();
        var tributo = CreateTributos().First();
        _inner.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Result<Tributo>.Success(tributo));

        await _sut.GetByIdAsync(id, CancellationToken.None);
        await _sut.GetByIdAsync(id, CancellationToken.None);

        await _inner.Received(2).GetByIdAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var tributos = CreateTributos();
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        await _sut.GetAllAsync(CancellationToken.None);
        _invalidator.InvalidateTributos();
        await _sut.GetAllAsync(CancellationToken.None);

        await _inner.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_FailureResult_ShouldNotCache()
    {
        var error = new Error("Test.Error", "test");
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Failure(error));

        await _sut.GetAllAsync(CancellationToken.None);
        await _sut.GetAllAsync(CancellationToken.None);

        await _inner.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_NoConfig_ShouldUseDefaultTtlOfTwentyFourHours()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var sut = new CachedTributoReadRepository(_inner, cache, _invalidator, new ConfigurationBuilder().Build());
        var tributos = CreateTributos();
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        await sut.GetAllAsync(CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task GetAllAsync_ConfigPresent_ShouldOverrideDefault()
    {
        var entry = Substitute.For<ICacheEntry>();
        entry.ExpirationTokens.Returns(new List<IChangeToken>());
        entry.PostEvictionCallbacks.Returns(new List<PostEvictionCallbackRegistration>());
        var cache = Substitute.For<IMemoryCache>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:Tributos"] = "1.00:00:00" })
            .Build();
        var sut = new CachedTributoReadRepository(_inner, cache, _invalidator, config);
        var tributos = CreateTributos();
        _inner.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        await sut.GetAllAsync(CancellationToken.None);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromDays(1));
    }

    private static IReadOnlyCollection<Tributo> CreateTributos()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        return new[] { Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value };
    }

    public void Dispose() => _cache.Dispose();
}
