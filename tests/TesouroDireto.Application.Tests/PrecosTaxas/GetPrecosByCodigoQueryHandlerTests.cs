using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecosByCodigoQueryHandlerTests
{
    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly IPrecoTaxaReadRepository _precoReadRepo = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecosByCodigoQueryHandler _handler;

    public GetPrecosByCodigoQueryHandlerTests()
    {
        _handler = new GetPrecosByCodigoQueryHandler(_tituloReadRepo, _precoReadRepo);
    }

    [Fact]
    public async Task Handle_WithValidCodigo_ShouldReturnPrecos()
    {
        var tituloId = Guid.NewGuid();
        var precos = new List<PrecoTaxaDto>
        {
            new("2025-03-23", 0.10m, 0.04m, 15800.00m, 15790.00m, 15785.00m),
            new("2025-03-24", 0.10m, 0.04m, 15810.00m, 15800.00m, 15795.00m)
        };

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithDateFilters_ShouldPassThrough()
    {
        var tituloId = Guid.NewGuid();
        var dataInicio = new DateOnly(2025, 1, 1);
        var dataFim = new DateOnly(2025, 3, 24);

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(new List<PrecoTaxaDto>()));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", dataInicio, dataFim, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _precoReadRepo.Received(1).GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownCodigo_ShouldReturnNotFound()
    {
        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2099-01-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.NotFound", "not found")));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2099-01-01", null, null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithMalformedCodigo_ShouldReturnInvalidCodigo()
    {
        _tituloReadRepo.GetIdByCodigoAsync("nao-e-um-codigo-valido", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(new Error("Titulo.InvalidCodigo", "malformed")));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("nao-e-um-codigo-valido", null, null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidCodigo");
    }

    [Fact]
    public async Task Handle_WithoutPage_ShouldReturnEntireCollectionAsTodayAndTotalCountEqualsItems()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 250)
            .Select(i => new PrecoTaxaDto($"2025-01-{i % 28 + 1:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(250);
        result.Value.TotalCount.Should().Be(250);
    }

    [Fact]
    public async Task Handle_WithPage_ShouldSliceUsingDefaultPageSize()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 250)
            .Select(i => new PrecoTaxaDto($"2025-{i / 28 + 1:D2}-{i % 28 + 1:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, 2, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(100);
        result.Value.TotalCount.Should().Be(250);
        result.Value.Items.Should().BeEquivalentTo(precos.Skip(100).Take(100), options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_WithPageAndPageSize_ShouldSliceAccordingly()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 30)
            .Select(i => new PrecoTaxaDto($"2025-01-{i:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, 3, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(10);
        result.Value.TotalCount.Should().Be(30);
        result.Value.Items.Should().BeEquivalentTo(precos.Skip(20).Take(10), options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_WithPageSizeAboveMax_ShouldClampTo500()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 600)
            .Select(i => new PrecoTaxaDto($"2025-{i / 28 + 1:D2}-{i % 28 + 1:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, 1, 1000), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(500);
        result.Value.TotalCount.Should().Be(600);
    }

    [Fact]
    public async Task Handle_WithPageSizeBelowMin_ShouldClampTo1()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 5)
            .Select(i => new PrecoTaxaDto($"2025-01-{i:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, 1, 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WithPageBelowMin_ShouldClampTo1()
    {
        var tituloId = Guid.NewGuid();
        var precos = Enumerable.Range(1, 5)
            .Select(i => new PrecoTaxaDto($"2025-01-{i:D2}", 0.1m, 0.1m, 100m, 100m, 100m))
            .ToList();

        _tituloReadRepo.GetIdByCodigoAsync("tesouro-selic-2029-03-01", Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(tituloId));
        _precoReadRepo.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(
            new GetPrecosByCodigoQuery("tesouro-selic-2029-03-01", null, null, 0, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(5);
        result.Value.TotalCount.Should().Be(5);
        result.Value.Items.Should().BeEquivalentTo(precos, options => options.WithStrictOrdering());
    }
}
