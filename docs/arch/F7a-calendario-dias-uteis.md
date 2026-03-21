# F7a — Calendario de Dias Uteis

## Contexto

O simulador de investimentos precisa calcular dias uteis (DU) entre duas datas para aplicar formulas de precificacao de titulos publicos. DU = dias corridos - fins de semana - feriados bancarios.

## Fonte de Dados

**ANBIMA** — `https://www.anbima.com.br/feriados/arqs/feriados_nacionais.xls`

### Formato do XLS

- Formato binario XLS (Compound Document File V2)
- Sheet unica: `Feriados`
- 3 colunas: `Data` (date serial), `Dia da Semana` (text), `Feriado` (text)
- ~1264 linhas de dados (2001-01-01 a 2099-12-25)
- Rows apos dados sao notas de texto (detectar por cell type != date)
- Inclui feriados que caem em fim de semana

### Parser

NuGet `ExcelDataReader` para ler `.xls` binario. Stream download via HttpClient.

## Arquitetura

### Domain — `Feriados/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `Feriado` | Entity\<Guid\> | Data + Descricao |
| `DataFeriado` | sealed record (VO) | DateOnly validado (nao MinValue) |
| `FeriadoErrors` | static class | Erros de dominio |

### Domain — `DiasUteis/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `DiasUteisCalculator` | sealed class | Calculo puro de DU entre duas datas dado conjunto de feriados |

Metodo: `int CalcularDiasUteis(DateOnly inicio, DateOnly fim, IReadOnlyCollection<DateOnly> feriados)`

Regras:
- Conta dias de `inicio+1` ate `fim` (inclusive)
- Exclui sabados e domingos
- Exclui feriados que caem em dias da semana
- Se inicio >= fim, retorna 0

### Application — `Feriados/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `IFeriadoReadRepository` | interface (port) | `GetAllDatasAsync` → `Result<IReadOnlyCollection<DateOnly>>` |
| `IFeriadoWriteRepository` | interface (port) | `AddRangeAsync`, `GetExistingDatasAsync` |
| `ImportFeriadosCommand` | sealed record | Trigger de importacao |
| `ImportFeriadosCommandHandler` | sealed class | Download + parse + upsert |
| `ImportFeriadosResult` | sealed record | FeriadosImportados, FeriadosIgnorados |
| `IFeriadoImportService` | interface (port) | Download + parse XLS ANBIMA |
| `IDiasUteisService` | interface (port) | Contrato: `CalcularDiasUteisAsync(DateOnly, DateOnly, CancellationToken)` |
| `DiasUteisService` | sealed class | Carrega feriados do repo + delega ao Calculator |

### Infrastructure — `Feriados/`

| Artefato | Tipo | Descricao |
|----------|------|-----------|
| `FeriadoReadRepository` | sealed (Dapper) | Query datas de feriados |
| `FeriadoWriteRepository` | sealed (EF Core) | Persiste feriados |
| `FeriadoImportService` | sealed (HttpClient) | Download XLS + parse com ExcelDataReader |
| `FeriadoConfiguration` | IEntityTypeConfiguration | Tabela `feriados` |

### API — `Endpoints/ImportacaoEndpoints.cs`

Adicionar `POST /importacao/feriados` no endpoint existente de importacao.

### Tabela `feriados`

| Coluna | Tipo | Constraint |
|--------|------|-----------|
| id | uuid | PK |
| data | date | UNIQUE, NOT NULL |
| descricao | varchar | NOT NULL |

### NuGet

- `ExcelDataReader` — leitura de XLS binario
- `ExcelDataReader.DataSet` — nao necessario (leitura streaming)

## Testes

- **Domain:** DataFeriado VO, Feriado.Create, DiasUteisCalculator (cenarios: mesmo dia, fins de semana, feriados, range longo)
- **Application:** ImportFeriadosCommandHandler, DiasUteisService
- **Integration:** Repositories, endpoint POST /importacao/feriados
