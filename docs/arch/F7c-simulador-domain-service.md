# F7c — Domain Service do Simulador

## Contexto

Servico de dominio puro (sem I/O) que calcula rendimento de investimentos em titulos do Tesouro Direto. Recebe todos os dados necessarios como input — quem orquestra I/O e o handler na Application (F7d).

## Design

### Domain — `Simulador/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `SimuladorService` | sealed class | Calcula rendimento bruto, aplica tributos, retorna resultado |
| `SimulacaoInput` | sealed record | Dados de entrada |
| `SimulacaoResultado` | sealed record | Resultado completo |
| `TributoAplicado` | sealed record | Tributo individual aplicado |
| `FluxoCupom` | sealed record | Cupom semestral individual |
| `SimuladorErrors` | static class | Erros de validacao |

### SimulacaoInput

```
TipoTitulo          — determina formula e se tem cupons
ValorInvestido      — decimal (> 0)
TaxaContratada      — decimal (% anual, ex: 12.5)
DataCompra          — DateOnly
DataVencimento      — DateOnly
DiasUteis           — int (DU entre compra e vencimento, ja calculado)
ProjecaoAnual       — decimal? (% anual, Selic/IPCA/IGPM projetado)
Feriados            — IReadOnlyCollection<DateOnly> (para DU dos cupons)
TributosAtivos      — IReadOnlyCollection<Tributo> (ordenados por Ordem)
```

### SimulacaoResultado

```
ValorInvestido      — decimal
ValorBruto          — decimal (valor no vencimento)
RendimentoBruto     — decimal (ValorBruto - ValorInvestido)
TributosAplicados   — IReadOnlyCollection<TributoAplicado>
TotalTributos       — decimal
ValorLiquido        — decimal (ValorBruto - TotalTributos)
RendimentoLiquido   — decimal (ValorLiquido - ValorInvestido)
Cupons              — IReadOnlyCollection<FluxoCupom>? (null se sem cupons)
```

## Formulas por Indexador

### Taxa Efetiva Anual

| Indexador | Formula |
|-----------|---------|
| Prefixado | `taxaContratada` |
| Selic | `(1 + selicProjetada/100) * (1 + spread/100) - 1` (Fisher) |
| IPCA | `(1 + ipcaProjetada/100) * (1 + taxaReal/100) - 1` (Fisher) |
| IGPM | `(1 + igpmProjetado/100) * (1 + taxaReal/100) - 1` (Fisher) |

Onde: `taxaContratada` = spread (Selic) ou taxa real (IPCA/IGPM)

### Rendimento Bruto (sem cupons)

```
ValorBruto = ValorInvestido * (1 + taxaEfetiva/100)^(DU/252)
```

### Cupons Semestrais

Para titulos com `PagaJurosSemestrais = true`:

**Taxa do cupom:**
- Prefixado c/ Juros: 10% a.a. → semestre = (1.10)^0.5 - 1 ≈ 4.881%
- IPCA+ c/ Juros: 6% a.a. → semestre = (1.06)^0.5 - 1 ≈ 2.956%
- IGP-M+ c/ Juros: 6% a.a. → semestre = (1.06)^0.5 - 1 ≈ 2.956%

**Datas de cupom:** a cada 6 meses contando do vencimento para tras.
Exemplo: vencimento 15/08/2035 → cupons em 15/02 e 15/08 de cada ano.

**Calculo:**
1. Gerar datas de cupom entre dataCompra e dataVencimento
2. PU_compra = soma(cupons_descontados) + principal_descontado
3. Quantidade = ValorInvestido / PU_compra
4. Cada cupom bruto = Quantidade * VNA * taxaCupomSemestral
5. No vencimento: Quantidade * VNA (principal) + ultimo cupom
6. ValorBruto = soma(todos os fluxos)

**Tributacao por cupom:**
Cada cupom e tributado individualmente com base nos dias corridos desde a data de compra ate a data do cupom.

## Aplicacao de Tributos

Iterar tributos ativos ordenados por Ordem:

1. Determinar **base de calculo** conforme `BaseCalculo`:
   - `Rendimento` → rendimento bruto (ou rendimento do cupom)
   - `PuBruto` → valor bruto total
   - `ValorInvestido` → valor investido original
   - `ValorResgate` → valor bruto (= PuBruto neste contexto)

2. Determinar **aliquota** conforme `TipoCalculo`:
   - `FaixaPorDias` → buscar faixa onde DiasMin <= dias <= DiasMax
   - `AliquotaFixa` → usar aliquota da primeira faixa
   - `TabelaDiaria` → buscar faixa onde Dia == dias

3. Calcular valor: `base * aliquota / 100`

4. Se `Cumulativo = true`: deduzir valor do rendimento para proximo tributo

## Testes Planejados

- Prefixado simples (1 teste)
- Selic simples (1 teste)
- IPCA+ simples (1 teste)
- IGP-M+ simples (1 teste)
- Prefixado c/ juros semestrais (1 teste)
- IPCA+ c/ juros semestrais (1 teste)
- Validacao: valor investido <= 0 (1 teste)
- Validacao: Selic/IPCA sem projecao (1 teste)
- Tributo IR por faixa de dias (3 testes: < 180d, 181-360d, > 720d)
- Tributo aliquota fixa (1 teste)
- Tributo cumulativo (1 teste)
- Sem tributos (1 teste)
- DiasUteis zero (1 teste)
