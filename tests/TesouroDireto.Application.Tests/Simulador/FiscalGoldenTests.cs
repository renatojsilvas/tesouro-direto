using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Simulador;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tests.Simulador;

public sealed class FiscalGoldenTests
{
    private readonly SimuladorService _service = new();
    private readonly IReadOnlyCollection<Tributo> _tributosAtivos = TributosPadrao.Build().Value;

    public static IEnumerable<object?[]> GoldenCases()
    {
        yield return new object?[] { 1, 96m, 960.00m, 22.5m, 9.00m, 969.00m, 10031.00m, 31.00m, 2 };
        yield return new object?[] { 15, 50m, 500.00m, 22.5m, 112.50m, 612.50m, 10387.50m, 387.50m, 2 };
        yield return new object?[] { 29, 3m, 30.00m, 22.5m, 218.25m, 248.25m, 10751.75m, 751.75m, 2 };
        yield return new object?[] { 30, null, null, 22.5m, 225.00m, 225.00m, 10775.00m, 775.00m, 1 };
        yield return new object?[] { 180, null, null, 22.5m, 225.00m, 225.00m, 10775.00m, 775.00m, 1 };
        yield return new object?[] { 181, null, null, 20m, 200.00m, 200.00m, 10800.00m, 800.00m, 1 };
        yield return new object?[] { 360, null, null, 20m, 200.00m, 200.00m, 10800.00m, 800.00m, 1 };
        yield return new object?[] { 361, null, null, 17.5m, 175.00m, 175.00m, 10825.00m, 825.00m, 1 };
        yield return new object?[] { 720, null, null, 17.5m, 175.00m, 175.00m, 10825.00m, 825.00m, 1 };
        yield return new object?[] { 721, null, null, 15m, 150.00m, 150.00m, 10850.00m, 850.00m, 1 };
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Simular_Golden_ShouldMatchExactExpectedValues(
        int diasCorridos,
        decimal? iofAliquotaEsperada,
        decimal? iofValorEsperado,
        decimal irAliquotaEsperada,
        decimal irValorEsperado,
        decimal totalTributosEsperado,
        decimal valorLiquidoEsperado,
        decimal rendimentoLiquidoEsperado,
        int countEsperado)
    {
        var resultado = Simular(diasCorridos);

        resultado.ValorBruto.Should().Be(11000.00m);
        resultado.RendimentoBruto.Should().Be(1000.00m);
        resultado.TributosAplicados.Should().HaveCount(countEsperado);
        resultado.TotalTributos.Should().Be(totalTributosEsperado);
        resultado.ValorLiquido.Should().Be(valorLiquidoEsperado);
        resultado.RendimentoLiquido.Should().Be(rendimentoLiquidoEsperado);

        var iof = resultado.TributosAplicados.SingleOrDefault(t => t.Nome == "IOF");

        if (iofAliquotaEsperada is null)
        {
            iof.Should().BeNull();
        }
        else
        {
            iof.Should().NotBeNull();
            iof!.Aliquota.Should().Be(iofAliquotaEsperada.Value);
            iof.Valor.Should().Be(iofValorEsperado!.Value);
        }

        var ir = resultado.TributosAplicados.Single(t => t.Nome == "Imposto de Renda");
        ir.Aliquota.Should().Be(irAliquotaEsperada);
        ir.Valor.Should().Be(irValorEsperado);
    }

    [Fact]
    public void Simular_Dia1_IofReduzBaseDoIrPara40()
    {
        var resultado = Simular(1);

        var iof = resultado.TributosAplicados.Single(t => t.Nome == "IOF");
        var ir = resultado.TributosAplicados.Single(t => t.Nome == "Imposto de Renda");

        iof.Aliquota.Should().Be(96m);
        iof.Valor.Should().Be(960.00m);
        ir.Base.Should().Be(40.00m);
        ir.Aliquota.Should().Be(22.5m);
        ir.Valor.Should().Be(9.00m);
        resultado.TotalTributos.Should().Be(969.00m);
        resultado.ValorLiquido.Should().Be(10031.00m);
        resultado.RendimentoLiquido.Should().Be(31.00m);
    }

    [Fact]
    public void Simular_Dia29_IofReduzBaseDoIrPara970()
    {
        var resultado = Simular(29);

        var iof = resultado.TributosAplicados.Single(t => t.Nome == "IOF");
        var ir = resultado.TributosAplicados.Single(t => t.Nome == "Imposto de Renda");

        iof.Aliquota.Should().Be(3m);
        iof.Valor.Should().Be(30.00m);
        ir.Base.Should().Be(970.00m);
        ir.Aliquota.Should().Be(22.5m);
        ir.Valor.Should().Be(218.25m);
        resultado.TotalTributos.Should().Be(248.25m);
        resultado.ValorLiquido.Should().Be(10751.75m);
        resultado.RendimentoLiquido.Should().Be(751.75m);
    }

    [Fact]
    public void IofValor_ShouldBeNonIncreasing_AcrossDias1To40()
    {
        for (var dia = 1; dia < 40; dia++)
        {
            var atual = IofValor(Simular(dia));
            var proximo = IofValor(Simular(dia + 1));

            proximo.Should().BeLessThanOrEqualTo(atual, $"IOF no dia {dia + 1} nao pode ser maior que no dia {dia}");
        }
    }

    [Fact]
    public void Iof_ShouldBeAbsent_FromDia30Onwards()
    {
        for (var dia = 30; dia <= 800; dia++)
        {
            var resultado = Simular(dia);

            resultado.TributosAplicados.Should().NotContain(t => t.Nome == "IOF", $"dia {dia} nao deveria ter IOF");
        }
    }

    [Fact]
    public void IrValor_ShouldNeverBeNegative()
    {
        decimal[] taxas = [5m, 10m, 20m];

        foreach (var taxa in taxas)
        {
            for (var dia = 1; dia <= 800; dia++)
            {
                var resultado = Simular(dia, taxa);

                IrValor(resultado).Should().BeGreaterThanOrEqualTo(0m, $"dia {dia}, taxa {taxa}");
            }
        }
    }

    [Fact]
    public void ValorLiquido_ShouldNeverExceedValorBruto()
    {
        decimal[] taxas = [5m, 10m, 20m];

        foreach (var taxa in taxas)
        {
            for (var dia = 1; dia <= 800; dia++)
            {
                var resultado = Simular(dia, taxa);

                resultado.ValorLiquido.Should().BeLessThanOrEqualTo(resultado.ValorBruto, $"dia {dia}, taxa {taxa}");
                resultado.TotalTributos.Should().BeGreaterThanOrEqualTo(0m, $"dia {dia}, taxa {taxa}");
            }
        }
    }

    [Fact]
    public void Aliquotas_ShouldChangeSmoothly_AcrossConsecutiveDias()
    {
        for (var dia = 1; dia < 800; dia++)
        {
            var atual = Simular(dia);
            var proximo = Simular(dia + 1);

            var deltaIof = Math.Abs(IofAliquota(proximo) - IofAliquota(atual));
            var deltaIr = Math.Abs(IrAliquota(proximo) - IrAliquota(atual));

            deltaIof.Should().BeLessThanOrEqualTo(4m, $"salto de IOF entre os dias {dia} e {dia + 1}");
            deltaIr.Should().BeLessThanOrEqualTo(2.5m, $"salto de IR entre os dias {dia} e {dia + 1}");
        }
    }

    private SimulacaoResultado Simular(int diasCorridos, decimal taxaContratada = 10m)
    {
        var dataCompra = new DateOnly(2024, 1, 2);
        var dataVencimento = dataCompra.AddDays(diasCorridos);

        var input = new SimulacaoInput(
            TipoTitulo.TesouroPrefixado,
            10_000m,
            taxaContratada,
            dataCompra,
            dataVencimento,
            252,
            diasCorridos,
            null,
            Array.Empty<DateOnly>(),
            _tributosAtivos);

        var result = _service.Simular(input);
        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    private static decimal IofValor(SimulacaoResultado resultado)
        => resultado.TributosAplicados.SingleOrDefault(t => t.Nome == "IOF")?.Valor ?? 0m;

    private static decimal IofAliquota(SimulacaoResultado resultado)
        => resultado.TributosAplicados.SingleOrDefault(t => t.Nome == "IOF")?.Aliquota ?? 0m;

    private static decimal IrValor(SimulacaoResultado resultado)
        => resultado.TributosAplicados.SingleOrDefault(t => t.Nome == "Imposto de Renda")?.Valor ?? 0m;

    private static decimal IrAliquota(SimulacaoResultado resultado)
        => resultado.TributosAplicados.SingleOrDefault(t => t.Nome == "Imposto de Renda")?.Aliquota ?? 0m;
}
