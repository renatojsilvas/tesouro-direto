# Design — Tarefa 9: Seed versionado de tributos e feriados

> Data: 2026-07-22 · Branch: `improvements` · Tarefa 9 do [PLANO](../../PLANO.md) · Risco: médio (dados fiscais)

## Problema

Deploy em banco novo sobe com as tabelas `tributos`/`tributo_faixas` e `feriados`
**vazias** (não há `HasData`, nem `.sql` de produção — só `tests/TesouroDireto.E2E.Tests/seed.sql`,
que é dado de teste). Resultado: o Simulador quebra (erro de tributo/feriado ausente)
até alguém popular manualmente — estado não versionado nem reproduzível.
Ver MAPA §4 (Verificação #4) e memória `project_tributos_configurados`.

## Objetivo

Tornar o estado inicial de tributos e feriados **reproduzível e automático** num banco
novo, sem passo manual, de forma **idempotente** (não duplica em banco já populado).
Critério de aceite (do plano): `docker compose down -v && up` → `POST /simulador`
retorna resultado válido, sem intervenção manual.

## Decisão de abordagem

Avaliadas 3 vias (registradas para posteridade):

| Via | Rejeitada porque |
|-----|------------------|
| Migration `HasData` | Bypassa a validação do domínio; exige 33 PKs `int` hardcoded para as faixas no snapshot; mudar valor = nova migration. |
| Script SQL idempotente + passo no `deploy.yml` | Não roda no `docker compose up` local (falha o critério de aceite); bypassa o domínio; faixas sem chave natural → idempotência frágil. |
| **Seeder na aplicação, via domínio (ESCOLHIDA)** | — |

**Escolhida: seeder na aplicação via CQRS/MediatR.** Motivos: (1) passa por
`Tributo.Create`/`Faixa.Create` → o **domínio valida os valores fiscais** (alíquota
0–100, faixas não-vazias, ordem); (2) roda logo após `MigrateAsync`, em Dev e Prod,
excluído em Testing — mesmo ciclo de vida das migrations (`feedback_migrations_always_run`);
(3) idempotência limpa pela existência (índice UNIQUE `ix_tributos_nome` já existe);
(4) alinhado a Ports & Adapters — grava pelo write repo port (`feedback_repos_result_pattern`);
(5) mudança de alíquota = mudança de código tipado com teste (vantagem para dado fiscal).
Trade-off aceito: uma query de existência no boot (desprezível); o seed é versionado em
git (lugar certo para **dados**, não schema), não no histórico de migration.

**Feriados**: não usar seed estático permanente (envelhece — feriados são publicados pela
ANBIMA e mudam). Em vez disso, disparar o `ImportFeriadosCommand` **existente** no primeiro
boot (tabela vazia), de forma **não-fatal**. A tarefa 10 (job Quartz) assume o refresh contínuo.

## Componentes

### 1. `SeedTributosCommand` + handler (novo, `Application/Tributos/`)
Espelha o padrão do `ImportFeriadosCommand` (record `IRequest<Result>`).
Handler (`SeedTributosCommandHandler`):
1. Consulta `ITributoReadRepository.GetAllAsync` (já existe; devolve as entidades `Tributo`).
   Se `Value.Count > 0` → **já semeado** → no-op, retorna `Result.Success()` (idempotente;
   roda todo boot).
2. Se vazio, para cada definição em `TributosPadrao`: constrói via `Tributo.Create`/`Faixa.Create`.
   Se qualquer `Create` retornar falha → propaga o `Result` de falha (valores canônicos
   malformados = bug a barrar no boot).
3. Persiste cada `Tributo` via `ITributoWriteRepository.AddAsync`. Propaga falha de persistência.

O `LoggingBehavior` (O3) já loga início/fim e captura `IsFailure` como Warning no caminho.

### 2. `TributosPadrao` (novo, estático, `Application/Tributos/`)
Valores canônicos como dados tipados, reproduzindo **exatamente** o `seed.sql` de E2E:

- **IOF** — `BaseCalculo.Rendimento`, `TipoCalculo.TabelaDiaria`, `Cumulativo = true`, `Ordem = 1`.
  29 faixas por `Dia` (1..29), `Aliquota` = `[96, 93, 90, 86, 83, 80, 76, 73, 70, 66, 63, 60,
  56, 53, 50, 46, 43, 40, 36, 33, 30, 26, 23, 20, 16, 13, 10, 6, 3]` (índice = Dia−1).
  `DiasMin`/`DiasMax` = null.
- **IR** — `BaseCalculo.Rendimento`, `TipoCalculo.FaixaPorDias`, `Cumulativo = false`, `Ordem = 2`.
  4 faixas por `[DiasMin, DiasMax]`: `(0,180)→22.5`, `(181,360)→20.0`, `(361,720)→17.5`,
  `(721,999999)→15.0`. `Dia` = null.

### 3. Feriados (reuso)
Nenhum componente novo. Reusa `ImportFeriadosCommand`/handler e `IFeriadoReadRepository`
(`GetAllDatasAsync` para detectar tabela vazia).

### 4. `InitializeDatabaseAsync` (novo, extensão em `API/Extensions/`)
Encapsula migrate + seed + import de primeiro boot, deixando o `Program.cs` fino.
Executa na ordem:
```
await db.Database.MigrateAsync();                         // schema
var seed = await sender.Send(new SeedTributosCommand());  // idempotente, todo boot
if (seed.IsFailure) throw ...;                            // FATAL

var datas = await feriadoRead.GetAllDatasAsync(ct);       // feriados: só 1º boot
if (datas.IsSuccess && datas.Value.Count == 0)
{
    var imp = await sender.Send(new ImportFeriadosCommand());
    if (imp.IsFailure) logger.LogWarning("Import de feriados no boot falhou: {Error}", ...);
    // NÃO derruba o boot; tarefa 10 (Quartz) refaz
}
```
O `Program.cs` troca o bloco atual (`using scope; MigrateAsync`) por `await app.InitializeDatabaseAsync();`,
mantendo o guard `if (!IsEnvironment("Testing"))` **dentro** da extensão.

## Fluxo de dados

```
boot (Dev/Prod, != Testing)
  └─ InitializeDatabaseAsync
       ├─ MigrateAsync ....................... schema pronto
       ├─ SeedTributosCommand ....... GetAllAsync.Count==0? ─sim→ Tributo.Create ×2 → ITributoWriteRepo.AddAsync
       │                                                └─não→ no-op
       └─ IFeriadoReadRepo.GetAllDatasAsync ── vazio? ─sim→ ImportFeriadosCommand (ANBIMA) ─falha→ LogWarning
                                                       └─não→ pula
```

## Tratamento de erro

| Cenário | Comportamento | Justificativa |
|---------|---------------|---------------|
| `Tributo.Create`/`Faixa.Create` retorna falha | **Fatal** — aborta o boot | Valores canônicos malformados = bug; barrar no deploy, não silenciar. |
| `ITributoWriteRepository.AddAsync` falha | **Fatal** — aborta o boot | DB indisponível/constraint = deploy quebrado. |
| Import de feriados falha (ANBIMA fora) | **Não-fatal** — `LogWarning`, boot segue | Fonte externa; tarefa 10 refaz; resiliência vem da 13. |
| Segundo boot (já semeado) | No-op idempotente | Existência por `ITributoReadRepository` / tabela de feriados não-vazia. |

## Testes

- **Unit** (`SeedTributosCommandHandlerTests`, novo): repo vazio (fake) → cria IOF (29 faixas)
  + IR (4 faixas) com alíquotas, `Cumulativo`, `Ordem`, `TipoCalculo` corretos; repo populado
  → no-op (nenhum `AddAsync`). Falha de `Create` (mock de valor inválido) → `Result` de falha.
- **Integração** (Testcontainers, padrão `API.Tests/.../Persistence/` ou `Integration/`): banco
  novo → executa o seed → assert tributos+faixas presentes e byte-corretos; executa **2×** →
  um só conjunto (idempotência real, não vacuosa).
- **Verificação manual** (critério do plano, não automatizada no CI): `docker compose down -v && up`
  → `POST /simulador` retorna resultado válido. Depende de feriados (ANBIMA no ar no 1º boot).

## Escopo / fora de escopo

- **Dentro**: `SeedTributosCommand`(+handler), `TributosPadrao`, `InitializeDatabaseAsync`,
  ajuste do `Program.cs`, testes unit+integração.
- **Fora**: refactor do `seed.sql` de E2E (mantém-se — tem titulos/precos de teste que o seed
  de prod não semeia); job Quartz de feriados (tarefa 10); resiliência ANBIMA (tarefa 13);
  seed de titulos/preços (não é dado fiscal estático — vem do import CSV).

## Riscos

- **Valores fiscais errados no `TributosPadrao`** → Simulador calcula errado silenciosamente.
  Mitigação: reproduzir exatamente o `seed.sql` de E2E (fonte atual validada) + teste de
  integração comparando faixas; domínio valida faixa de alíquota.
- **ANBIMA fora no primeiro boot** → feriados vazios até a tarefa 10. Aceito (não-fatal),
  documentado; verificação manual assume ANBIMA no ar.
