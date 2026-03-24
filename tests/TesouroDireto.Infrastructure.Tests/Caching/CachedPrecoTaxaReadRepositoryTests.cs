using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CachedPrecoTaxaReadRepositoryTests : IDisposable
{
    private readonly IPrecoTaxaReadRepository _inner = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly CachedPrecoTaxaReadRepository _sut;

    public CachedPrecoTaxaReadRepositoryTests()
    {
        _sut = new CachedPrecoTaxaReadRepository(_inner, _cache, _invalidator);
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_CacheMiss_ShouldCallInnerAndCache()
    {
        var tituloId = Guid.NewGuid();
        var preco = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 11.50m, 11.55m, 1050.00m, 1049.00m, 1048.50m);
        _inner.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco));

        var result = await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(preco);
        await _inner.Received(1).GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_CacheHit_ShouldNotCallInner()
    {
        var tituloId = Guid.NewGuid();
        var preco = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 11.50m, 11.55m, 1050.00m, 1049.00m, 1048.50m);
        _inner.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco));

        await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);
        var result = await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _inner.Received(1).GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_DifferentTitulos_ShouldUseDifferentCacheKeys()
    {
        var tituloId1 = Guid.NewGuid();
        var tituloId2 = Guid.NewGuid();
        var preco1 = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 11.50m, 11.55m, 1050.00m, 1049.00m, 1048.50m);
        var preco2 = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 6.00m, 6.05m, 800.00m, 799.00m, 798.50m);

        _inner.GetLatestByTituloIdAsync(tituloId1, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco1));
        _inner.GetLatestByTituloIdAsync(tituloId2, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco2));

        var result1 = await _sut.GetLatestByTituloIdAsync(tituloId1, CancellationToken.None);
        var result2 = await _sut.GetLatestByTituloIdAsync(tituloId2, CancellationToken.None);

        result1.Value.PuCompra.Should().Be(1050.00m);
        result2.Value.PuCompra.Should().Be(800.00m);
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_AfterInvalidation_ShouldCallInnerAgain()
    {
        var tituloId = Guid.NewGuid();
        var preco = new PrecoTaxaDto(Guid.NewGuid(), "2025-03-24", 11.50m, 11.55m, 1050.00m, 1049.00m, 1048.50m);
        _inner.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(preco));

        await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);
        _invalidator.InvalidatePrecos();
        await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);

        await _inner.Received(2).GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_FailureResult_ShouldNotCache()
    {
        var tituloId = Guid.NewGuid();
        var error = new Error("PrecoTaxa.NotFound", "not found");
        _inner.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Failure(error));

        await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);
        await _sut.GetLatestByTituloIdAsync(tituloId, CancellationToken.None);

        await _inner.Received(2).GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TituloExistsAsync_ShouldAlwaysCallInner()
    {
        var tituloId = Guid.NewGuid();
        _inner.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        await _sut.TituloExistsAsync(tituloId, CancellationToken.None);
        await _sut.TituloExistsAsync(tituloId, CancellationToken.None);

        await _inner.Received(2).TituloExistsAsync(tituloId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByTituloIdAsync_ShouldAlwaysCallInner()
    {
        var tituloId = Guid.NewGuid();
        var precos = new List<PrecoTaxaDto>();
        _inner.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        await _sut.GetByTituloIdAsync(tituloId, null, null, CancellationToken.None);
        await _sut.GetByTituloIdAsync(tituloId, null, null, CancellationToken.None);

        await _inner.Received(2).GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _cache.Dispose();
}
