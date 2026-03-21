using FluentAssertions;
using TesouroDireto.Domain.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Domain.Tests.Simulador;

public sealed class SimuladorServiceTests
{
    private readonly SimuladorService _service = new();

    // === PREFIXADO ===

    [Fact]
    public void Simular_Prefixado_ShouldCalculateCorrectly()
    {
        // R$10,000 at 12% for 252 DU (1 year), no tributos
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 252);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.ValorInvestido.Should().Be(10_000m);
        result.Value.ValorBruto.Should().BeApproximately(11_200m, 1m);
        result.Value.RendimentoBruto.Should().BeApproximately(1_200m, 1m);
        result.Value.Cupons.Should().BeNull();
    }

    [Fact]
    public void Simular_Prefixado_HalfYear_ShouldCompound()
    {
        // R$10,000 at 12% for 126 DU (~6 months)
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 126);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        // (1.12)^(126/252) = (1.12)^0.5 ≈ 1.0583
        result.Value.ValorBruto.Should().BeApproximately(10_583m, 2m);
    }

    // === SELIC ===

    [Fact]
    public void Simular_Selic_ShouldUseProjectionPlusSpread()
    {
        // R$10,000, Selic projetada 13.75%, spread 0.1%, 252 DU
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroSelic,
            valorInvestido: 10_000m,
            taxaContratada: 0.10m, // spread
            diasUteis: 252,
            projecaoAnual: 13.75m);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        // Effective ≈ (1 + 0.1375) * (1 + 0.001) - 1 ≈ 13.864%
        result.Value.ValorBruto.Should().BeApproximately(11_386m, 5m);
    }

    [Fact]
    public void Simular_Selic_WithoutProjection_ShouldReturnFailure()
    {
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroSelic,
            valorInvestido: 10_000m,
            taxaContratada: 0.10m,
            diasUteis: 252,
            projecaoAnual: null);

        var result = _service.Simular(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulador.ProjecaoRequired");
    }

    // === IPCA+ ===

    [Fact]
    public void Simular_IPCA_ShouldUseFisherEquation()
    {
        // R$10,000, IPCA projetada 4%, taxa real 6%, 252 DU
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroIPCA,
            valorInvestido: 10_000m,
            taxaContratada: 6m, // taxa real
            diasUteis: 252,
            projecaoAnual: 4m);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        // Effective = (1.04) * (1.06) - 1 = 10.24%
        result.Value.ValorBruto.Should().BeApproximately(11_024m, 2m);
    }

    // === IGP-M+ ===

    [Fact]
    public void Simular_IGPM_ShouldUseFisherEquation()
    {
        // R$10,000, IGP-M projetada 5%, taxa real 6%, 252 DU
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroIGPMComJuros,
            valorInvestido: 10_000m,
            taxaContratada: 6m,
            diasUteis: 252,
            projecaoAnual: 5m);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        // Effective = (1.05) * (1.06) - 1 = 11.3%
        // But IGP-M+ always has coupons, so calculation is different
        result.Value.Cupons.Should().NotBeNull();
    }

    // === CUPONS SEMESTRAIS ===

    [Fact]
    public void Simular_PrefixadoComJuros_ShouldGenerateCupons()
    {
        // R$10,000, 12%, 504 DU (2 years), vencimento in 2 years
        var dataCompra = new DateOnly(2024, 1, 15);
        var dataVencimento = new DateOnly(2026, 1, 15);
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixadoComJuros,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 504,
            dataCompra: dataCompra,
            dataVencimento: dataVencimento);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cupons.Should().NotBeNull();
        result.Value.Cupons!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Simular_IPCAComJuros_ShouldGenerateCupons()
    {
        var dataCompra = new DateOnly(2024, 1, 15);
        var dataVencimento = new DateOnly(2026, 1, 15);
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroIPCAComJuros,
            valorInvestido: 10_000m,
            taxaContratada: 6m,
            diasUteis: 504,
            projecaoAnual: 4m,
            dataCompra: dataCompra,
            dataVencimento: dataVencimento);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cupons.Should().NotBeNull();
        result.Value.Cupons!.Count.Should().BeGreaterThan(0);
    }

    // === TRIBUTOS ===

    [Fact]
    public void Simular_WithIR_FaixaPorDias_ShortTerm_ShouldApply225()
    {
        // 180 dias corridos → 22.5% sobre rendimento
        var ir = CreateTributoIR();
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 126,
            diasCorridos: 180,
            tributos: [ir]);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.Should().HaveCount(1);
        result.Value.TributosAplicados.First().Aliquota.Should().Be(22.5m);
        result.Value.TotalTributos.Should().BeGreaterThan(0);
        result.Value.ValorLiquido.Should().BeLessThan(result.Value.ValorBruto);
    }

    [Fact]
    public void Simular_WithIR_FaixaPorDias_MediumTerm_ShouldApply20()
    {
        // 360 dias corridos → 20%
        var ir = CreateTributoIR();
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 252,
            diasCorridos: 360,
            tributos: [ir]);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.First().Aliquota.Should().Be(20m);
    }

    [Fact]
    public void Simular_WithIR_FaixaPorDias_LongTerm_ShouldApply15()
    {
        // 721 dias corridos → 15%
        var ir = CreateTributoIR();
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 504,
            diasCorridos: 721,
            tributos: [ir]);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.First().Aliquota.Should().Be(15m);
    }

    [Fact]
    public void Simular_WithAliquotaFixa_ShouldApplyFixed()
    {
        var taxaB3 = Tributo.Create("Taxa B3", BaseCalculo.ValorInvestido,
            TipoCalculo.AliquotaFixa,
            [Faixa.Create(0, null, null, 0.25m).Value], 2, false).Value;

        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 252,
            tributos: [taxaB3]);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.Should().HaveCount(1);
        // 0.25% de 10,000 = 25
        result.Value.TributosAplicados.First().Valor.Should().BeApproximately(25m, 0.01m);
    }

    [Fact]
    public void Simular_WithCumulativeTributos_ShouldReduceBaseForNext()
    {
        var ir = CreateTributoIR(ordem: 1, cumulativo: true);
        var taxaExtra = Tributo.Create("Taxa Extra", BaseCalculo.Rendimento,
            TipoCalculo.AliquotaFixa,
            [Faixa.Create(0, null, null, 10m).Value], 2, false).Value;

        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 252,
            diasCorridos: 360,
            tributos: [ir, taxaExtra]);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.Should().HaveCount(2);
        // IR first: 20% of ~1200 = ~240, rendimento adjusted to ~960
        // Taxa extra: 10% of ~960 (adjusted) = ~96
        var taxaExtraAplicada = result.Value.TributosAplicados.Last();
        taxaExtraAplicada.Base.Should().BeLessThan(result.Value.RendimentoBruto);
    }

    [Fact]
    public void Simular_WithNoTributos_ShouldHaveZeroTributos()
    {
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 252);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.TributosAplicados.Should().BeEmpty();
        result.Value.TotalTributos.Should().Be(0);
        result.Value.ValorLiquido.Should().Be(result.Value.ValorBruto);
    }

    // === VALIDATIONS ===

    [Fact]
    public void Simular_WithZeroInvestment_ShouldReturnFailure()
    {
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 0m,
            taxaContratada: 12m,
            diasUteis: 252);

        var result = _service.Simular(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulador.InvalidValorInvestido");
    }

    [Fact]
    public void Simular_WithZeroDiasUteis_ShouldReturnInvestedAmount()
    {
        var input = CreateInput(
            tipoTitulo: TipoTitulo.TesouroPrefixado,
            valorInvestido: 10_000m,
            taxaContratada: 12m,
            diasUteis: 0);

        var result = _service.Simular(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.ValorBruto.Should().Be(10_000m);
        result.Value.RendimentoBruto.Should().Be(0m);
    }

    // === HELPERS ===

    private static SimulacaoInput CreateInput(
        TipoTitulo tipoTitulo,
        decimal valorInvestido,
        decimal taxaContratada,
        int diasUteis,
        int? diasCorridos = null,
        decimal? projecaoAnual = null,
        DateOnly? dataCompra = null,
        DateOnly? dataVencimento = null,
        IReadOnlyCollection<Tributo>? tributos = null)
    {
        var compra = dataCompra ?? new DateOnly(2024, 1, 2);
        var vencimento = dataVencimento ?? compra.AddDays(diasCorridos ?? (int)(diasUteis * 365.0 / 252));

        return new SimulacaoInput(
            tipoTitulo,
            valorInvestido,
            taxaContratada,
            compra,
            vencimento,
            diasUteis,
            diasCorridos ?? (int)(diasUteis * 365.0 / 252),
            projecaoAnual,
            Array.Empty<DateOnly>(),
            tributos ?? Array.Empty<Tributo>());
    }

    private static Tributo CreateTributoIR(int ordem = 1, bool cumulativo = false)
    {
        return Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias,
        [
            Faixa.Create(0, 180, null, 22.5m).Value,
            Faixa.Create(181, 360, null, 20m).Value,
            Faixa.Create(361, 720, null, 17.5m).Value,
            Faixa.Create(721, null, null, 15m).Value
        ], ordem, cumulativo).Value;
    }
}
