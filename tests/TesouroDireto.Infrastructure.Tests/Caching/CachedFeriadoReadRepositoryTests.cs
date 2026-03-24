using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedFeriadoReadRepositoryTests : IDisposable
{
    private readonly IFeriadoReadRepository _inner = Substitute.For<IFeriadoReadRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly CachedFeriadoReadRepository _sut;

    public CachedFeriadoReadRepositoryTests()
    {
        _sut = new CachedFeriadoReadRepository(_inner, _cache, _invalidator);
    }

    [Fact]
    public async Task GetAllDatasAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var datas = new List<DateOnly> { new(2025, 1, 1), new(2025, 4, 21) };
        _inner.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(datas));

        var result = await _sut.GetAllDatasAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _inner.Received(1).GetAllDatasAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDatasAsync_CacheHit_ShouldNotCallInner()
    {
        var datas = new List<DateOnly> { new(2025, 1, 1) };
        _inner.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(datas));

        await _sut.GetAllDatasAsync(CancellationToken.None);
        await _sut.GetAllDatasAsync(CancellationToken.None);

        await _inner.Received(1).GetAllDatasAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDatasAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var datas = new List<DateOnly> { new(2025, 1, 1) };
        _inner.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(datas));

        await _sut.GetAllDatasAsync(CancellationToken.None);
        _invalidator.InvalidateFeriados();
        await _sut.GetAllDatasAsync(CancellationToken.None);

        await _inner.Received(2).GetAllDatasAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDatasAsync_FailureResult_ShouldNotCache()
    {
        var error = new Error("Test.Error", "test");
        _inner.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Failure(error));

        await _sut.GetAllDatasAsync(CancellationToken.None);
        await _sut.GetAllDatasAsync(CancellationToken.None);

        await _inner.Received(2).GetAllDatasAsync(Arg.Any<CancellationToken>());
    }

    public void Dispose() => _cache.Dispose();
}
