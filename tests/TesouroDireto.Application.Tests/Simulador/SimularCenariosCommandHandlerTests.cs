using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
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
    private const string Codigo = "tesouro-ipca-mais-2025-01-02";

    private readonly ITituloReadRepository _tituloReadRepo = Substitute.For<ITituloReadRepository>();
    private readonly ITituloWriteRepository _tituloRepo = Substitute.For<ITituloWriteRepository>();
    private readonly IDiasUteisService _diasUteisService = Substitute.For<IDiasUteisService>();
    private readonly ITributoReadRepository _tributoRepo = Substitute.For<ITributoReadRepository>();
    private readonly IBusinessMetrics _metrics = Substitute.For<IBusinessMetrics>();
    private readonly SimularCenariosCommandHandler _handler;

    public SimularCenariosCommandHandlerTests()
    {
        var simuladorService = new SimuladorApplicationService(
            _tituloReadRepo, _tituloRepo, _diasUteisService, _tributoRepo);
        _handler = new SimularCenariosCommandHandler(simuladorService, _metrics);

        _tributoRepo.GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(Array.Empty<Tributo>()));

        _diasUteisService.CalcularDiasUteisAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Result<DiasUteisFeriados>.Success(new DiasUteisFeriados(252, Array.Empty<DateOnly>())));
    }

    [Fact]
    public async Task Handle_MultipleCenarios_ShouldReturnResultForEach()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroIPCA, new DateOnly(2025, 1, 2));
        _tituloReadRepo.GetIdByCodigoAsync(Codigo, Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(titulo.Id));
        _tituloRepo.GetByIdAsync(titulo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(titulo));

        var cenarios = new[]
        {
            new CenarioInput("Otimista", 3m),
            new CenarioInput("Base", 5m),
            new CenarioInput("Pessimista", 7m)
        };

        var command = new SimularCenariosCommand(
            Codigo, 10_000m, new DateOnly(2024, 1, 2), 6m, cenarios);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(r => r.Nome).Should().ContainInOrder("Otimista", "Base", "Pessimista");

        var valores = result.Value.Select(r => r.Resultado.ValorBruto).ToList();
        valores[0].Should().BeLessThan(valores[1]);
        valores[1].Should().BeLessThan(valores[2]);
    }

    [Fact]
    public async Task Handle_MultipleCenarios_ShouldRecordSimulationPerCenario()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroIPCA, new DateOnly(2025, 1, 2));
        _tituloReadRepo.GetIdByCodigoAsync(Codigo, Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(titulo.Id));
        _tituloRepo.GetByIdAsync(titulo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(titulo));

        var cenarios = new[]
        {
            new CenarioInput("Otimista", 3m),
            new CenarioInput("Base", 5m),
            new CenarioInput("Pessimista", 7m)
        };

        var command = new SimularCenariosCommand(
            Codigo, 10_000m, new DateOnly(2024, 1, 2), 6m, cenarios);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _metrics.Received(3).RecordSimulation("IPCA", "success");
        _metrics.Received(3).RecordSimulation(Arg.Any<string>(), "success");
        _metrics.DidNotReceive().RecordSimulationFailure(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_TituloNotFound_ShouldReturnFailure()
    {
        _tituloReadRepo.GetIdByCodigoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(TituloErrors.NotFound));

        var command = new SimularCenariosCommand(
            "tesouro-selic-2099-01-01", 10_000m, new DateOnly(2024, 1, 2), 6m,
            [new CenarioInput("Test", 5m)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MalformedCodigo_ShouldReturnFailure()
    {
        _tituloReadRepo.GetIdByCodigoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure(TituloErrors.InvalidCodigo));

        var command = new SimularCenariosCommand(
            "nao-e-um-codigo-valido", 10_000m, new DateOnly(2024, 1, 2), 6m,
            [new CenarioInput("Test", 5m)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.InvalidCodigo");
    }

    private static Titulo CreateTitulo(TipoTitulo tipoTitulo, DateOnly vencimento)
    {
        var dv = DataVencimento.Create(vencimento).Value;
        return Titulo.Create(tipoTitulo, dv).Value;
    }
}
