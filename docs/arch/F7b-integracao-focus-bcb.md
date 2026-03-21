# F7b — Integracao API Focus BCB

## Contexto

O simulador precisa de projecoes de mercado (Selic, IPCA, IGP-M) para calcular rendimento futuro de titulos indexados. O usuario pode informar projecao ou usar o consenso do mercado (Boletim Focus/BCB).

## Fonte de Dados

**API Focus BCB** (publica, sem auth, formato JSON OData)

Base: `https://olinda.bcb.gov.br/olinda/servico/Expectativas/versao/v1/odata/`

### Selic

`ExpectativasMercadoSelic?$top=1&$orderby=Data desc&$format=json`

```json
{
  "value": [{
    "Indicador": "Selic",
    "Data": "2026-03-13",
    "Reuniao": "R4/2026",
    "Media": 14.75,
    "Mediana": 14.75
  }]
}
```

### IPCA / IGP-M (12 meses)

`ExpectativasMercadoInflacao12Meses?$top=1&$filter=Indicador eq '{indicador}'&$orderby=Data desc&$format=json`

```json
{
  "value": [{
    "Indicador": "IPCA",
    "Data": "2026-03-13",
    "Suavizada": "N",
    "Media": 3.97,
    "Mediana": 3.98
  }]
}
```

## Arquitetura

### Application — `Projecoes/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `IProjecaoMercadoService` | interface (port) | `GetProjecaoAsync(Indexador, CancellationToken)` → `Result<ProjecaoMercado>` |
| `ProjecaoMercado` | sealed record (DTO) | Indicador, DataReferencia, MediaAnual, MedianaAnual |

### Infrastructure — `Projecoes/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `FocusBcbService` | sealed class | HttpClient para API Focus, parse JSON, mapeamento |

### Decisoes

- **Sem entidade de dominio** — projecoes sao dados externos, nao pertencem ao dominio
- **Sem persistencia** — projecoes mudam diariamente, buscar on-demand
- **Sem endpoint novo** — servico interno consumido pelo simulador (F7c/F7d)
- **Mediana** como valor principal (padrao do mercado)
- **Cache em memoria** — projecoes nao mudam intraday, cachear por 1 hora

## Testes

- **Application:** N/A (interface apenas)
- **Integration:** FocusBcbService com FakeHttpMessageHandler + JSON de teste
  - Parse correto de Selic
  - Parse correto de IPCA
  - Parse correto de IGP-M
  - Erro quando API retorna vazio
  - Erro quando API falha (HTTP 500)
  - Validacao HTTPS
