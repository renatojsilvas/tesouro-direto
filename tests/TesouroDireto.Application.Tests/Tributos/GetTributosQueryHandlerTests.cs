using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class GetTributosQueryHandlerTests
{
    private readonly ITributoReadRepository _repository = Substitute.For<ITributoReadRepository>();
    private readonly GetTributosQueryHandler _handler;

    public GetTributosQueryHandlerTests()
    {
        _handler = new GetTributosQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_ShouldReturnAllTributos()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        IReadOnlyCollection<Tributo> tributos = new[]
        {
            Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value,
            Tributo.Create("IOF", BaseCalculo.Rendimento, TipoCalculo.TabelaDiaria, faixas, 2, false).Value
        };

        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        var result = await _handler.Handle(new GetTributosQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ShouldMapDtoFieldsCorrectly()
    {
        var faixas = new[]
        {
            Faixa.Create(0, 180, null, 22.5m).Value,
            Faixa.Create(181, 360, null, 20m).Value
        };

        IReadOnlyCollection<Tributo> tributos = new[]
        {
            Tributo.Create("Imposto de Renda", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, true).Value
        };

        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        var result = await _handler.Handle(new GetTributosQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.First();
        dto.Nome.Should().Be("Imposto de Renda");
        dto.BaseCalculo.Should().Be("Rendimento");
        dto.TipoCalculo.Should().Be("FaixaPorDias");
        dto.Faixas.Should().HaveCount(2);
        dto.Ativo.Should().BeTrue();
        dto.Ordem.Should().Be(1);
        dto.Cumulativo.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNoTributos_ShouldReturnEmptyCollection()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(Array.Empty<Tributo>()));

        var result = await _handler.Handle(new GetTributosQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldMapFaixaDtoCorrectly()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        IReadOnlyCollection<Tributo> tributos = new[]
        {
            Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value
        };

        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(tributos));

        var result = await _handler.Handle(new GetTributosQuery(), CancellationToken.None);

        var faixaDto = result.Value.First().Faixas.First();
        faixaDto.DiasMin.Should().Be(0);
        faixaDto.DiasMax.Should().Be(180);
        faixaDto.Dia.Should().BeNull();
        faixaDto.Aliquota.Should().Be(22.5m);
    }
}
