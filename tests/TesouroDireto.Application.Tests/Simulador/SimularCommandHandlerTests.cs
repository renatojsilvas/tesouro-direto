using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Simulador;

public sealed class SimularCommandHandlerTests
{
    private readonly ITituloWriteRepository _tituloRepo = Substitute.For<ITituloWriteRepository>();
    private readonly IDiasUteisService _diasUteisService = Substitute.For<IDiasUteisService>();
    private readonly IProjecaoMercadoService _projecaoService = Substitute.For<IProjecaoMercadoService>();
    private readonly ITributoReadRepository _tributoRepo = Substitute.For<ITributoReadRepository>();
    private readonly IFeriadoReadRepository _feriadoRepo = Substitute.For<IFeriadoReadRepository>();
    private readonly SimularCommandHandler _handler;

    public SimularCommandHandlerTests()
    {
        _handler = new SimularCommandHandler(
            _tituloRepo, _diasUteisService, _projecaoService, _tributoRepo, _feriadoRepo);

        _tributoRepo.GetAtivosOrdenadosAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(Array.Empty<Tributo>()));

        _feriadoRepo.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));
    }

    [Fact]
    public async Task Handle_Prefixado_ShouldReturnSimulacao()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroPrefixado, new DateOnly(2025, 1, 2));
        SetupTitulo(titulo);
        SetupDiasUteis(252);

        var command = new SimularCommand(titulo.Id, 10_000m, new DateOnly(2024, 1, 2), 12m, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ValorInvestido.Should().Be(10_000m);
        result.Value.ValorBruto.Should().BeGreaterThan(10_000m);
        await _projecaoService.DidNotReceive()
            .GetProjecaoAsync(Arg.Any<Indexador>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TituloNotFound_ShouldReturnFailure()
    {
        _tituloRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Failure(TituloErrors.NotFound));

        var command = new SimularCommand(Guid.NewGuid(), 10_000m, new DateOnly(2024, 1, 2), 12m, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_SelicWithoutProjecao_ShouldFetchFromFocus()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroSelic, new DateOnly(2025, 1, 2));
        SetupTitulo(titulo);
        SetupDiasUteis(252);
        _projecaoService.GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>())
            .Returns(Result<ProjecaoMercado>.Success(
                new ProjecaoMercado("Selic", new DateOnly(2024, 1, 1), 13.75m, 13.75m)));

        var command = new SimularCommand(titulo.Id, 10_000m, new DateOnly(2024, 1, 2), 0.10m, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _projecaoService.Received(1)
            .GetProjecaoAsync(Indexador.Selic, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SelicWithProjecao_ShouldNotFetchFromFocus()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroSelic, new DateOnly(2025, 1, 2));
        SetupTitulo(titulo);
        SetupDiasUteis(252);

        var command = new SimularCommand(titulo.Id, 10_000m, new DateOnly(2024, 1, 2), 0.10m, 13.75m);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _projecaoService.DidNotReceive()
            .GetProjecaoAsync(Arg.Any<Indexador>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidValor_ShouldReturnFailure()
    {
        var titulo = CreateTitulo(TipoTitulo.TesouroPrefixado, new DateOnly(2025, 1, 2));
        SetupTitulo(titulo);
        SetupDiasUteis(252);

        var command = new SimularCommand(titulo.Id, -100m, new DateOnly(2024, 1, 2), 12m, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static Titulo CreateTitulo(TipoTitulo tipoTitulo, DateOnly vencimento)
    {
        var dv = DataVencimento.Create(vencimento).Value;
        return Titulo.Create(tipoTitulo, dv).Value;
    }

    private void SetupTitulo(Titulo titulo)
    {
        _tituloRepo.GetByIdAsync(titulo.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Titulo>.Success(titulo));
    }

    private void SetupDiasUteis(int du)
    {
        _diasUteisService.CalcularDiasUteisAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(du));
    }
}
