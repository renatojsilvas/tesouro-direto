using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Projecoes;

public sealed class CachedProjecaoMercadoServiceTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxFallbackAge = TimeSpan.FromDays(7);

    private readonly FakeTimeProvider _timeProvider =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly MemoryCache _cache;
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly IProjecaoMercadoService _inner = Substitute.For<IProjecaoMercadoService>();
    private readonly ILogger<CachedProjecaoMercadoService> _logger =
        Substitute.For<ILogger<CachedProjecaoMercadoService>>();
    private readonly CachedProjecaoMercadoService _sut;

    public CachedProjecaoMercadoServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { Clock = new FakeSystemClock(_timeProvider) });

        var configuration = new ConfigurationBuilder().Build();

        _sut = new CachedProjecaoMercadoService(
            _inner, _cache, _invalidator, _timeProvider, configuration, _logger);
    }

    public void Dispose() => _cache.Dispose();

    private sealed class FakeSystemClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }

    [Fact]
    public async Task GetProjecaoAsync_MultipleCallsWithinTtl_ShouldHitInnerOnlyOnce()
    {
        SetupInnerSuccess(Indexador.Selic);

        for (var i = 0; i < 5; i++)
        {
            var result = await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await _inner.Received(1).GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProjecaoAsync_AfterTtlExpires_ShouldHitInnerAgain()
    {
        SetupInnerSuccess(Indexador.Selic);

        await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        _timeProvider.Advance(Ttl + TimeSpan.FromMinutes(1));

        await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        await _inner.Received(2).GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProjecaoAsync_WhenFreshExpiresAndBcbFails_ShouldFallBackToLastKnownGood()
    {
        SetupInnerSuccess(Indexador.Selic, mediana: 13.75m);

        var first = await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        _timeProvider.Advance(Ttl + TimeSpan.FromMinutes(1));

        _inner.GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Failure(ProjecaoErrors.HttpError));

        var second = await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.Origem.Should().Be(OrigemProjecao.CacheFallback);
        second.Value.MedianaAnual.Should().Be(first.Value.MedianaAnual);
        second.Value.Indicador.Should().Be(first.Value.Indicador);
        second.Value.ObtidaEmUtc.Should().Be(first.Value.ObtidaEmUtc);

        AssertWarningLogged();
    }

    [Fact]
    public async Task GetProjecaoAsync_WithSizeLimitedCache_ShouldNotThrow_BecauseFreshAndLkgEntriesSpecifySize()
    {
        using var sizeLimitedCache = new MemoryCache(
            new MemoryCacheOptions { SizeLimit = 10, Clock = new FakeSystemClock(_timeProvider) });
        var sut = new CachedProjecaoMercadoService(
            _inner, sizeLimitedCache, _invalidator, _timeProvider, new ConfigurationBuilder().Build(), _logger);
        SetupInnerSuccess(Indexador.Selic);

        var act = async () => await sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        await act.Should().NotThrowAsync<InvalidOperationException>(
            "as duas entradas escritas por chamada (fresh e lkg) precisam declarar Size " +
            "quando o IMemoryCache tem SizeLimit configurado — sem isso o MemoryCache lança " +
            "InvalidOperationException em runtime no primeiro cache miss");
    }

    [Fact]
    public async Task GetProjecaoAsync_ColdCacheAndHttpError_ShouldReturnHttpError()
    {
        _inner.GetProjecaoAsync(Indexador.IPCA, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Failure(ProjecaoErrors.HttpError));

        var result = await _sut.GetProjecaoAsync(Indexador.IPCA, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProjecaoErrors.HttpError);
    }

    [Fact]
    public async Task GetProjecaoAsync_WhenFallbackOlderThanMaxAge_ShouldFail()
    {
        SetupInnerSuccess(Indexador.Selic);

        await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        _timeProvider.Advance(MaxFallbackAge + TimeSpan.FromHours(1));

        _inner.GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Failure(ProjecaoErrors.HttpError));

        var result = await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProjecaoErrors.HttpError);
    }

    [Fact]
    public async Task GetProjecaoAsync_NotFound_ShouldPropagateWithoutFallback_EvenWithHotLkg()
    {
        await AssertPropagatesWithoutFallback(ProjecaoErrors.NotFound);
    }

    [Fact]
    public async Task GetProjecaoAsync_UrlNotConfigured_ShouldPropagateWithoutFallback_EvenWithHotLkg()
    {
        await AssertPropagatesWithoutFallback(ProjecaoErrors.UrlNotConfigured);
    }

    [Fact]
    public async Task GetProjecaoAsync_IndexadorNaoSuportado_ShouldPropagateWithoutFallback_EvenWithHotLkg()
    {
        await AssertPropagatesWithoutFallback(ProjecaoErrors.IndexadorNaoSuportado);
    }

    [Fact]
    public async Task GetProjecaoAsync_ShouldIsolateCacheByIndexador()
    {
        SetupInnerSuccess(Indexador.Selic, indicador: "Selic", mediana: 13m);
        await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        SetupInnerSuccess(Indexador.IPCA, indicador: "IPCA", mediana: 4m);
        var ipcaResult = await _sut.GetProjecaoAsync(Indexador.IPCA, CancellationToken.None);

        ipcaResult.IsSuccess.Should().BeTrue();
        ipcaResult.Value.Indicador.Should().Be("IPCA");
        await _inner.Received(1).GetProjecaoAsync(Indexador.IPCA, Arg.Any<CancellationToken>());
        await _inner.Received(1).GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>());
    }

    private async Task AssertPropagatesWithoutFallback(Error error)
    {
        SetupInnerSuccess(Indexador.Selic);
        await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        _timeProvider.Advance(Ttl + TimeSpan.FromMinutes(1));

        _inner.GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Failure(error));

        var result = await _sut.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    private void SetupInnerSuccess(
        Indexador indexador, string indicador = "Selic", decimal mediana = 13.75m)
    {
        var projecao = new ProjecaoMercado(
            indicador,
            new DateOnly(2026, 1, 1),
            mediana,
            mediana,
            _timeProvider.GetUtcNow(),
            OrigemProjecao.Bcb);

        _inner.GetProjecaoAsync(indexador, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Success(projecao));
    }

    private void AssertWarningLogged()
    {
        var warningCalls = _logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && call.GetArguments()[0] is LogLevel.Warning)
            .ToList();

        warningCalls.Should().HaveCount(1);
    }
}
