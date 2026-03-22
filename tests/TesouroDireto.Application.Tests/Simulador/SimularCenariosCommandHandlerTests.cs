using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Simulador;

public sealed class SimularCenariosCommandHandlerTests
{
    private readonly ITituloWriteRepository _tituloRepo = Substitute.For<ITituloWriteRepository>();
    private readonly IDiasUteisService _diasUteisService = Substitute.For<IDiasUteisService>();
    private readonly ITributoReadRepository _tributoRepo = Substitute.For<ITributoReadRepository>();
    private readonly IFeriadoReadRepository _feriadoRepo = Substitute.For<IFeriadoReadRepository>();
    private readonly SimularCenariosCommandHandler _handler;

    public SimularCenariosCommandHandlerTests()
    {
        _handler = new SimularCenariosCommandHandler(
            _tituloRepo, _diasUteisService, _tributoRepo, _feriadoRepo);

        _tributoRepo.GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(Array.Empty<Tributo>()));

        _feriadoRepo.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));

        _diasUteisService.CalcularDiasUteisAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(252));
    }

    [Fact]
    public async Task Handle_MultipleCenarios_ShouldReturnResultForEach()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroIPCA, new DateOnly(2025, 1, 2));
        _tituloRepo.GetByIdAsync(titulo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(titulo));

        var cenarios = new[]
        {
            new CenarioInput("Otimista", 3m),
            new CenarioInput("Base", 5m),
            new CenarioInput("Pessimista", 7m)
        };

        var command = new SimularCenariosCommand(
            titulo.Id, 10_000m, new DateOnly(2024, 1, 2), 6m, cenarios);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(r => r.Nome).Should().ContainInOrder("Otimista", "Base", "Pessimista");

        // Higher projection = higher return
        var valores = result.Value.Select(r => r.Resultado.ValorBruto).ToList();
        valores[0].Should().BeLessThan(valores[1]);
        valores[1].Should().BeLessThan(valores[2]);
    }

    [Fact]
    public async Task Handle_TituloNotFound_ShouldReturnFailure()
    {
        _tituloRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Failure(TituloErrors.NotFound));

        var command = new SimularCenariosCommand(
            Guid.NewGuid(), 10_000m, new DateOnly(2024, 1, 2), 6m,
            [new CenarioInput("Test", 5m)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static Titulo CreateTitulo(TipoTitulo tipoTitulo, DateOnly vencimento)
    {
        var dv = DataVencimento.Create(vencimento).Value;
        return Titulo.Create(tipoTitulo, dv).Value;
    }
}
