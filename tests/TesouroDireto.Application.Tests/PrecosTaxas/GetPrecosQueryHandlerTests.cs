using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecosQueryHandlerTests
{
    private readonly IPrecoTaxaReadRepository _repository = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecosQueryHandler _handler;

    public GetPrecosQueryHandlerTests()
    {
        _handler = new GetPrecosQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithValidTitulo_ShouldReturnPrecos()
    {
        var tituloId = Guid.NewGuid();
        IReadOnlyCollection<PrecoTaxaDto> precos = new[]
        {
            new PrecoTaxaDto(Guid.NewGuid(), "2024-01-02", 13.12m, 13.18m, 756.43m, 755.39m, 756.43m),
            new PrecoTaxaDto(Guid.NewGuid(), "2024-01-03", 13.10m, 13.16m, 757.00m, 756.00m, 757.00m)
        };

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        _repository.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(new GetPrecosQuery(tituloId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithDateFilters_ShouldPassToRepository()
    {
        var tituloId = Guid.NewGuid();
        var dataInicio = new DateOnly(2024, 6, 1);
        var dataFim = new DateOnly(2024, 6, 30);
        IReadOnlyCollection<PrecoTaxaDto> precos = new[]
        {
            new PrecoTaxaDto(Guid.NewGuid(), "2024-06-15", 10.0m, 10.5m, 900m, 899m, 898m)
        };

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        _repository.GetByTituloIdAsync(tituloId, dataInicio, dataFim, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos));

        var result = await _handler.Handle(new GetPrecosQuery(tituloId, dataInicio, dataFim), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithNonExistentTitulo_ShouldReturnNotFound()
    {
        var tituloId = Guid.NewGuid();

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        var result = await _handler.Handle(new GetPrecosQuery(tituloId, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithEmptyGuid_ShouldReturnNotFound()
    {
        var result = await _handler.Handle(new GetPrecosQuery(Guid.Empty, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithNoPrecos_ShouldReturnEmptyCollection()
    {
        var tituloId = Guid.NewGuid();

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        _repository.GetByTituloIdAsync(tituloId, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(Array.Empty<PrecoTaxaDto>()));

        var result = await _handler.Handle(new GetPrecosQuery(tituloId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
