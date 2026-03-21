# F7d-1 — POST /simulador endpoint

## Contexto

Endpoint que orquestra as 3 foundations (F7a dias uteis, F7b projecoes, F7c calculo) para simular um investimento em titulo do Tesouro Direto.

## Request

```json
POST /simulador
{
  "tituloId": "guid",
  "valorInvestido": 10000.00,
  "dataCompra": "2024-01-02",
  "taxaContratada": 12.0,
  "projecaoAnual": 4.0       // opcional
}
```

## Response

```json
{
  "valorInvestido": 10000.00,
  "valorBruto": 11024.00,
  "rendimentoBruto": 1024.00,
  "tributosAplicados": [
    { "nome": "IR", "base": 1024.00, "aliquota": 20.0, "valor": 204.80 }
  ],
  "totalTributos": 204.80,
  "valorLiquido": 10819.20,
  "rendimentoLiquido": 819.20,
  "cupons": null
}
```

## Arquitetura

### Application — `Simulador/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `SimularCommand` | sealed record | TituloId, ValorInvestido, DataCompra, TaxaContratada, ProjecaoAnual? |
| `SimularCommandHandler` | sealed class | Orquestra I/O + domain service |
| `SimulacaoResultadoDto` | sealed record | DTO do resultado |
| `TributoAplicadoDto` | sealed record | DTO do tributo aplicado |
| `FluxoCupomDto` | sealed record | DTO do cupom |

### Handler — fluxo

```
1. ITituloWriteRepository.GetByIdAsync(tituloId) → Titulo
2. IDiasUteisService.CalcularDiasUteisAsync(dataCompra, dataVencimento) → DU
3. DiasCorridos = dataVencimento.ToDateTime() - dataCompra.ToDateTime()
4. Se projecaoAnual is null E indexador != Prefixado:
   → IProjecaoMercadoService.GetProjecaoAsync(indexador) → ProjecaoMercado
   → projecaoAnual = projecaoMercado.MedianaAnual
5. ITributoReadRepository.GetAtivosOrdenadosAsync() → tributos
6. IFeriadoReadRepository.GetAllDatasAsync() → feriados
7. SimuladorService.Simular(input) → SimulacaoResultado
8. Map to DTO
```

### Delta no codebase existente

- **ITituloWriteRepository**: adicionar `GetByIdAsync(Guid, CancellationToken)`
- **TituloWriteRepository**: implementar
- **API**: novo `SimuladorEndpoints.cs` com `MapSimuladorEndpoints()`
- **Program.cs**: registrar endpoints

## Testes

- **Unit (handler):** titulo encontrado + prefixado, titulo nao encontrado, selic sem projecao (busca Focus), selic com projecao (nao busca Focus), valor invalido
- **Integration:** endpoint POST /simulador com resposta valida
