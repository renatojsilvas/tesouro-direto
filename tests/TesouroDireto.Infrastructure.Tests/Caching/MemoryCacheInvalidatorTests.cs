using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class MemoryCacheInvalidatorTests
{
    private readonly MemoryCacheInvalidator _invalidator = new();

    [Fact]
    public void InvalidateTitulos_ShouldCancelPreviousToken()
    {
        var token = _invalidator.GetTitulosToken();
        token.IsCancellationRequested.Should().BeFalse();

        _invalidator.InvalidateTitulos();

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void InvalidateTitulos_ShouldProvideNewToken()
    {
        var oldToken = _invalidator.GetTitulosToken();

        _invalidator.InvalidateTitulos();

        var newToken = _invalidator.GetTitulosToken();
        newToken.Should().NotBeSameAs(oldToken);
        newToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void InvalidatePrecos_ShouldCancelPreviousToken()
    {
        var token = _invalidator.GetPrecosToken();

        _invalidator.InvalidatePrecos();

        token.IsCancellationRequested.Should().BeTrue();
        _invalidator.GetPrecosToken().IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void InvalidateTributos_ShouldCancelPreviousToken()
    {
        var token = _invalidator.GetTributosToken();

        _invalidator.InvalidateTributos();

        token.IsCancellationRequested.Should().BeTrue();
        _invalidator.GetTributosToken().IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void InvalidateFeriados_ShouldCancelPreviousToken()
    {
        var token = _invalidator.GetFeriadosToken();

        _invalidator.InvalidateFeriados();

        token.IsCancellationRequested.Should().BeTrue();
        _invalidator.GetFeriadosToken().IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void InvalidateTitulos_ShouldExpireCacheEntries()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(_invalidator.GetTitulosToken()));

        cache.Set("titulos:all:all", "cached-value", options);
        cache.TryGetValue("titulos:all:all", out _).Should().BeTrue();

        _invalidator.InvalidateTitulos();

        cache.TryGetValue("titulos:all:all", out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidateTitulos_ShouldNotAffectOtherDomains()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var titulosOptions = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(_invalidator.GetTitulosToken()));
        var tributosOptions = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(_invalidator.GetTributosToken()));

        cache.Set("titulos:key", "titulos-value", titulosOptions);
        cache.Set("tributos:key", "tributos-value", tributosOptions);

        _invalidator.InvalidateTitulos();

        cache.TryGetValue("titulos:key", out _).Should().BeFalse();
        cache.TryGetValue("tributos:key", out _).Should().BeTrue();
    }
}
