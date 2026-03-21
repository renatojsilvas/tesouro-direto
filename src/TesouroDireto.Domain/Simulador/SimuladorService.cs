using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.DiasUteis;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Domain.Simulador;

public sealed class SimuladorService
{
    private static readonly DiasUteisCalculator DuCalculator = new();

    public Result<SimulacaoResultado> Simular(SimulacaoInput input)
    {
        if (input.ValorInvestido <= 0)
        {
            return SimuladorErrors.InvalidValorInvestido;
        }

        if (input.DiasUteis <= 0)
        {
            return BuildResultado(input, input.ValorInvestido, null);
        }

        var indexador = input.TipoTitulo.Indexador;

        if (indexador != Indexador.Prefixado && input.ProjecaoAnual is null)
        {
            return SimuladorErrors.ProjecaoRequired;
        }

        var taxaEfetiva = CalcularTaxaEfetiva(indexador, input.TaxaContratada, input.ProjecaoAnual);

        if (input.TipoTitulo.PagaJurosSemestrais)
        {
            return SimularComCupons(input, taxaEfetiva);
        }

        var valorBruto = CalcularValorFuturo(input.ValorInvestido, taxaEfetiva, input.DiasUteis);
        return BuildResultado(input, valorBruto, null);
    }

    private static decimal CalcularTaxaEfetiva(Indexador indexador, decimal taxaContratada, decimal? projecaoAnual)
    {
        if (indexador == Indexador.Prefixado)
        {
            return taxaContratada;
        }

        // Fisher equation: (1 + nominal) = (1 + real) * (1 + inflacao)
        var projecao = projecaoAnual!.Value / 100m;
        var taxa = taxaContratada / 100m;
        var efetiva = (1m + projecao) * (1m + taxa) - 1m;
        return efetiva * 100m;
    }

    private static decimal CalcularValorFuturo(decimal valorPresente, decimal taxaAnualPercent, int diasUteis)
    {
        var taxa = (double)(taxaAnualPercent / 100m);
        var expoente = (double)diasUteis / 252.0;
        var fator = Math.Pow(1.0 + taxa, expoente);
        return valorPresente * (decimal)fator;
    }

    private Result<SimulacaoResultado> SimularComCupons(SimulacaoInput input, decimal taxaEfetiva)
    {
        var datasCupom = GerarDatasCupom(input.DataCompra, input.DataVencimento);
        var taxaCupomSemestral = ObterTaxaCupomSemestral(input.TipoTitulo);

        // Calculate PU at purchase: sum of discounted future cash flows
        var puCompra = CalcularPuComCupons(
            taxaEfetiva, taxaCupomSemestral, datasCupom,
            input.DataCompra, input.Feriados);

        var quantidade = input.ValorInvestido / puCompra;
        var cupons = new List<FluxoCupom>();
        var valorBrutoTotal = 0m;

        foreach (var dataCupom in datasCupom)
        {
            var duCupom = DuCalculator.Calcular(input.DataCompra, dataCupom, input.Feriados);
            var valorCupom = quantidade * 1000m * taxaCupomSemestral;

            cupons.Add(new FluxoCupom(dataCupom, Math.Round(valorCupom, 2), duCupom));
            valorBrutoTotal += valorCupom;
        }

        // Principal at maturity
        var principal = quantidade * 1000m;
        valorBrutoTotal += principal;

        return BuildResultado(input, valorBrutoTotal, cupons);
    }

    private static decimal CalcularPuComCupons(
        decimal taxaEfetiva,
        decimal taxaCupomSemestral,
        IReadOnlyCollection<DateOnly> datasCupom,
        DateOnly dataCompra,
        IReadOnlyCollection<DateOnly> feriados)
    {
        var taxa = (double)(taxaEfetiva / 100m);
        var pu = 0.0;

        foreach (var dataCupom in datasCupom)
        {
            var du = DuCalculator.Calcular(dataCompra, dataCupom, feriados);
            var expoente = (double)du / 252.0;
            var cupomDescontado = (double)(1000m * taxaCupomSemestral) / Math.Pow(1.0 + taxa, expoente);
            pu += cupomDescontado;
        }

        // Principal discounted
        var lastCupom = datasCupom.Last();
        var duPrincipal = DuCalculator.Calcular(dataCompra, lastCupom, feriados);
        var principalDescontado = 1000.0 / Math.Pow(1.0 + taxa, (double)duPrincipal / 252.0);
        pu += principalDescontado;

        return (decimal)pu;
    }

    private static IReadOnlyCollection<DateOnly> GerarDatasCupom(DateOnly dataCompra, DateOnly dataVencimento)
    {
        var datas = new List<DateOnly>();
        var current = dataVencimento;

        // Walk backwards from maturity in 6-month steps
        while (current > dataCompra)
        {
            datas.Add(current);
            current = current.AddMonths(-6);
        }

        datas.Sort();
        return datas;
    }

    private static decimal ObterTaxaCupomSemestral(TipoTitulo tipoTitulo)
    {
        if (tipoTitulo == TipoTitulo.TesouroPrefixadoComJuros)
        {
            // 10% annual → semester = (1.10)^0.5 - 1
            return (decimal)(Math.Pow(1.10, 0.5) - 1.0);
        }

        // IPCA+ and IGP-M+ with coupons: 6% annual → semester = (1.06)^0.5 - 1
        return (decimal)(Math.Pow(1.06, 0.5) - 1.0);
    }

    private static Result<SimulacaoResultado> BuildResultado(
        SimulacaoInput input, decimal valorBruto, IReadOnlyCollection<FluxoCupom>? cupons)
    {
        valorBruto = Math.Round(valorBruto, 2);
        var rendimentoBruto = Math.Round(valorBruto - input.ValorInvestido, 2);
        var tributosAplicados = AplicarTributos(input, rendimentoBruto, valorBruto);
        var totalTributos = Math.Round(tributosAplicados.Sum(t => t.Valor), 2);
        var valorLiquido = Math.Round(valorBruto - totalTributos, 2);
        var rendimentoLiquido = Math.Round(valorLiquido - input.ValorInvestido, 2);

        return new SimulacaoResultado(
            input.ValorInvestido,
            valorBruto,
            rendimentoBruto,
            tributosAplicados,
            totalTributos,
            valorLiquido,
            rendimentoLiquido,
            cupons);
    }

    private static IReadOnlyCollection<TributoAplicado> AplicarTributos(
        SimulacaoInput input, decimal rendimentoBruto, decimal valorBruto)
    {
        if (input.TributosAtivos.Count == 0)
        {
            return Array.Empty<TributoAplicado>();
        }

        var aplicados = new List<TributoAplicado>();
        var rendimentoAjustado = rendimentoBruto;

        foreach (var tributo in input.TributosAtivos.OrderBy(t => t.Ordem))
        {
            var baseCalculo = tributo.BaseCalculo switch
            {
                BaseCalculo.Rendimento => rendimentoAjustado,
                BaseCalculo.PuBruto => valorBruto,
                BaseCalculo.ValorInvestido => input.ValorInvestido,
                BaseCalculo.ValorResgate => valorBruto,
                _ => rendimentoAjustado
            };

            var aliquota = ObterAliquota(tributo, input.DiasCorridos);
            if (aliquota is null)
            {
                continue;
            }

            var valor = Math.Round(baseCalculo * aliquota.Value / 100m, 2);
            aplicados.Add(new TributoAplicado(tributo.Nome, Math.Round(baseCalculo, 2), aliquota.Value, valor));

            if (tributo.Cumulativo)
            {
                rendimentoAjustado -= valor;
            }
        }

        return aplicados;
    }

    private static decimal? ObterAliquota(Tributo tributo, int diasCorridos)
    {
        return tributo.TipoCalculo switch
        {
            TipoCalculo.FaixaPorDias => tributo.Faixas
                .Where(f =>
                    (f.DiasMin is null || diasCorridos >= f.DiasMin) &&
                    (f.DiasMax is null || diasCorridos <= f.DiasMax))
                .Select(f => (decimal?)f.Aliquota)
                .FirstOrDefault(),

            TipoCalculo.AliquotaFixa => tributo.Faixas
                .Select(f => (decimal?)f.Aliquota)
                .FirstOrDefault(),

            TipoCalculo.TabelaDiaria => tributo.Faixas
                .Where(f => f.Dia == diasCorridos)
                .Select(f => (decimal?)f.Aliquota)
                .FirstOrDefault(),

            _ => null
        };
    }
}
