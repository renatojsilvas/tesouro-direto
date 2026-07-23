# Mapa do Sistema — Tesouro Direto API

> Mapeamento por área (rotas/entradas, modelo de dados, integrações externas, jobs/observabilidade, testes).
> Gerado por análise **somente-leitura** do código. Não altera comportamento.
> As fragilidades marcadas com ✅ foram confirmadas contra o código pelo revisor; ⚠️ = nuance; ❌ = refutada. **✔️ RESOLVIDO** = já corrigida (tarefa do [PLANO](./PLANO.md) + data). Ver seção [Verificação](#verificação-das-fragilidades).

## Arquitetura em uma frase

Solução .NET 8 em **Clean Architecture / Ports & Adapters**, mono-domínio com contextos (Titulos, PrecosTaxas, Tributos, Simulador, Feriados, DiasUteis). CQRS via **MediatR**: escritas com **EF Core**, leituras com **Dapper** (devolvendo DTOs). Duas entradas: **API Minimal** (`TesouroDireto.API`) e **Blazor Server** (`TesouroDireto.Web`) que consome a API por HTTP. Ingestão de dados por **importação agendada (Quartz)** de fontes públicas (Tesouro Transparente, ANBIMA, BCB Focus). Observabilidade com Serilog→Loki + Prometheus→Grafana.

```
                 ┌─────────────────┐         ┌──────────────────┐
  navegador ───► │ Web (Blazor SSR) │──HTTP──►│  API (Minimal)   │
                 └─────────────────┘ X-Api-Key└────────┬─────────┘
                                                        │ ISender (MediatR)
                                            ┌───────────▼───────────┐
                                            │      Application       │  commands/queries + ports
                                            └───┬───────────────┬────┘
                          EF Core (write) ◄─────┘               └─────► Dapper (read, +cache decorators)
                                                      ┌───────────────────────┐
   Tesouro Transparente (CSV)           ──Quartz────►│    Infrastructure     │──► PostgreSQL
   ANBIMA (XLS, Quartz anual + manual)  ──HTTP───────►│  adapters / persist.  │
   BCB Focus (OData, cache 6h + lkg 7d) ──HTTP───────►└───────────────────────┘
```

Projetos: `API`, `Web`, `Application`, `Domain`, `Infrastructure` + 6 projetos de teste.

---

## 1. Rotas / Entradas

### O que existe

**API — Minimal API** (`src/TesouroDireto.API/Program.cs`). Pipeline: `AddSerilog` → `AddInfrastructure` → `AddMediatR` → `AddHealthChecks().AddDbContextCheck` → **`ApiKeyGuard.Validate` (aborta o boot em ambiente != Development/Testing se `ApiKey:Key` for vazia ou o default — antes da migração)** → migração automática (exceto env `Testing`) → `UseSerilogDefaults` (CorrelationId + request logging) → `UseHttpMetrics` → `ApiKeyMiddleware` → rotas. Cada endpoint é **fino**: injeta `ISender`, faz `Send(command/query)` e traduz `Result`→`Results.*`. Sem lógica de negócio nos endpoints.

| Método | Rota | Comando/Query | Status |
|--------|------|---------------|--------|
| GET | `/health`, `/health/ready` | `AddDbContextCheck` (readiness, isento de ApiKey) | 200/503 |
| GET | `/health/live` | liveness (`Predicate=_=>false`, não toca DB, isento) | 200 |
| GET | `/metrics` | Prometheus (isento) | 200 |
| POST | `/importacao` | `ImportCsvCommand` (sem body) | 200/400 |
| POST | `/importacao/feriados` | `ImportFeriadosCommand` (sem body) | 200/400 |
| GET | `/titulos?indexador&vencido` | `GetTitulosQuery` | 200/400 |
| GET | `/titulos/{id}/precos?dataInicio&dataFim` | `GetPrecosQuery` | 200/404 |
| GET | `/titulos/{id}/preco-atual` | `GetPrecoAtualQuery` | 200/404 |
| GET | `/titulos/preco-atual?nome` | `GetPrecoAtualByNomeQuery` | 200/404 |
| GET | `/titulos/precos?nome&dataInicio&dataFim` | `GetPrecosByNomeQuery` | 200/404 |
| GET | `/configuracoes/tributos` | `GetTributosQuery` | 200/400 |
| POST | `/configuracoes/tributos` | **`CreateTributoCommand` (corpo ligado direto)** | 201/400 |
| PUT | `/configuracoes/tributos/{id}` | `UpdateTributoCommand` (via `UpdateTributoRequest`) | 204/404/400 |
| POST | `/simulador` | `SimularCommand` (via `SimularRequest`) | 200/400 |
| POST | `/simulador/cenarios` | `SimularCenariosCommand` | 200/400 |

**Middleware:** `ApiKeyMiddleware` (header `X-Api-Key`, comparação SHA256 em tempo constante, isenta `/health` e `/metrics`, falha→401 com corpo `application/problem+json`+`correlationId` desde a tarefa 6); `CorrelationIdMiddleware` (header `X-Correlation-Id`, valida regex, injeta no LogContext **e em `HttpContext.Items`** para leitura no corpo do ProblemDetails).

**Web — Blazor Server** (`src/TesouroDireto.Web`): Razor Components (InteractiveServer) + `HttpClient` nomeado `"TesouroDiretoApi"` (BaseUrl + header `X-Api-Key` de config). Páginas em `Components/Pages/`: `Titulos`, `Historico` (gráfico via JS interop), `Tributos`, `Simulador`, `Cenarios`, `Home`, `About`, `Error`. As pastas `Pages/` e `Services/` estão **vazias** — cada página injeta `IHttpClientFactory` e chama a API direto.

### Como se conecta

- **API → Application:** dois `IPipelineBehavior` registrados (ordem = mais externo primeiro): `LoggingBehavior` (Application, tarefa O3 — loga início/fim e captura `Result.IsFailure` como Warning via a interface `IResult` que `Result`/`Result<T>` implementam; nunca lança) e depois `CacheInvalidationBehavior` (Infra — invalida cache em comandos de escrita, testando `response is IResult r && r.IsSuccess`). **Não há ValidationBehavior/FluentValidation** — validação vive em cada handler, devolvendo `Result` de falha.
- **Web → API:** 100% HTTP, autenticação por `X-Api-Key` compartilhado (Web `ApiSettings:ApiKey` == API `ApiKey:Key`). Blazor server-side → chamada sai do servidor, navegador nunca vê a chave; sem CORS.
- **Contrato de erro:** falha devolve `{ Code, Description }`; cada página desserializa num record local `ApiError`.

### Fragilidades
- **Contratos vazando/duplicados** ✔️ RESOLVIDO parcialmente (tarefa 15): `POST /configuracoes/tributos` agora binda `CreateTributoRequest` (`API/Contracts/`), não mais o `CreateTributoCommand` da Application. Resíduo: `Create/UpdateTributoRequest` ainda referenciam `FaixaDto` da Application — movido para "Fora de escopo por ora" no PLANO.
- **Sem camada de acesso no Web** ✅ (tarefa 16, pendente): `Web/Services/` vazio; lógica de acesso à API (CreateClient, request anônimo, desserialização, `ApiError`) **duplicada nas 5 páginas**.
- **Enums só numéricos no JSON** ✔️ RESOLVIDO (tarefa 4): `JsonStringEnumConverter` registrado (aceita string E número); `ParseEnum` hardcoded removido do `Tributos.razor`.
- **Erro por string-matching** ✔️ RESOLVIDO (tarefa 7, ver acima): o `Error.Code.Contains("NotFound")` do PUT foi eliminado pelo helper `ToHttpResult`.
- **Sem validação declarativa na borda** — exception handler global ✔️ RESOLVIDO (tarefa 6/O4): `UseExceptionHandler` + `AddProblemDetails` → exceção crua vira `application/problem+json` com `correlationId`/`traceId`; 401 do `ApiKeyMiddleware` também padronizado.
- **Sem versionamento, sem Swagger/OpenAPI, sem rate limiting** nos endpoints (inclui `POST /importacao`, que dispara download/parse externo). `GET /` retorna `"Hello World!"`; `AllowedHosts: "*"`.
- **Mapeamento `Result`→HTTP** ✔️ RESOLVIDO (tarefa 7): helper `ToHttpResult` em `Extensions/ResultExtensions.cs`, mapa por estrutura (`.NotFound`→404, resto→400) aplicado nos 4 `Endpoints/*.cs` — fim do `Contains("NotFound")` e do boilerplate por endpoint.

---

## 2. Modelo de Dados

### O que existe

**Domínio** (`src/TesouroDireto.Domain`), por contexto, tudo herdando `Common/Entity.cs` (igualdade por `Id`) e usando Result Pattern (`Common/Result.cs`, `Error.cs`, `DomainErrors.cs`):

- **Titulos** — agregado `Titulo` (`Entity<Guid>`, factory `Create`, ctor privado). VOs: `TipoTitulo` (8 instâncias), `Indexador` (whitelist de 4), `DataVencimento`. `Titulo` deriva `Indexador` e `PagaJurosSemestrais` do `TipoTitulo` (via pattern matching, `DeriveIndexador`).
- **PrecosTaxas** — agregado `PrecoTaxa` (referencia `Titulo` por `TituloId`, não navegação). VOs nuláveis: `Taxa`, `PrecoUnitario` (exige `> 0`), `DataBase`. Todos os 6 campos financeiros opcionais.
- **Tributos** — agregado rico `Tributo` com coleção encapsulada `_faixas`, métodos `Ativar/Desativar/AtualizarFaixas`. VO `Faixa` (record: DiasMin/DiasMax/Dia/Aliquota). Enums `BaseCalculo`, `TipoCalculo`. Único agregado com ctor sem parâmetros para EF.
- **Feriados** — `Feriado` + VO `DataFeriado`.
- **Simulador / DiasUteis** — objetos de cálculo transitórios, **não persistidos** (sem DbSet).

**Persistência** (`src/TesouroDireto.Infrastructure/Persistence`): `AppDbContext` implementa `IUnitOfWork`; 4 DbSets (Titulos, PrecosTaxas, Tributos, Feriados). Tabelas snake_case:
- `titulos` (PK uuid `ValueGeneratedNever`; índice único `ix_titulos_tipo_vencimento` em tipo+vencimento; desde a tarefa 12: `ix_titulos_data_vencimento` para filtro "vencido" e `ix_titulos_nome_upper` — índice funcional que torna `GetByNomeAsync` sargável)
- `precos_taxas` (FK `titulo_id` CASCADE; índice único `ix_precos_taxas_titulo_data`; valores numeric nuláveis)
- `tributos` + `tributo_faixas` (`OwnsMany`; índice único `ix_tributos_nome`)
- `feriados` (índice único `ix_feriados_data`)

3 migrations (`InitialCreate`, `MakePrecoTaxaValuesNullable`, `AddFeriados`) + snapshot alinhado (sem drift aparente).

### Como se conecta

- **Dapper (read → DTO):** `TituloReadRepository`, `PrecoTaxaReadRepository`, `FeriadoReadRepository` — SQL cru, `NpgsqlDataSource` singleton, `DateOnlyTypeHandler` custom. Todos embrulhados por decorators de cache.
- **EF Core (write):** `*WriteRepository` (Add/Update/Exists); `SaveChanges` só via `IUnitOfWork`.
- **Exceção ao padrão:** `TributoReadRepository` usa **EF e devolve a entidade de domínio** (não DTO) — Tributo é agregado com coleção owned consumida pelo Simulador.
- **Mapeamento de VOs:** `HasConversion` nas configs — VOs → string/decimal via `.Value` na materialização; `Faixas` como `OwnsMany`; enums como string.

### Fragilidades
- **VO `Indexador` com whitelist rígida quebra materialização EF** ✔️ RESOLVIDO (tarefa 2, 2026-07-19): novo factory `Indexador.FromPersistence(string)` (lossless, sem-falha) usado só na leitura do EF (`TituloConfiguration`); `FromName` permanece estrito para validação de filtro (contrato de API intacto). Valor fora da whitelist na coluna não quebra mais a materialização. `TipoTitulo` já era mitigado por pattern matching. **Resíduo (tarefa 22):** a coluna `indexador` segue `HasMaxLength(20)` — a leitura ficou tolerante, mas um valor bruto >20 chars na escrita ainda truncaria/estouraria.
- **Converters `.Value` sem tratamento de falha**: `DataVencimento/DataBase/PrecoUnitario.Create(v).Value` assumem sucesso; um `pu` ≤ 0 no banco faria a materialização EF lançar (hoje mascarado porque reads passam por Dapper).
- **VO `Taxa` sem invariante**: `Create` devolve `Taxa` direto (não `Result`), aceita qualquer decimal — inconsistente com os demais VOs.
- **Invariantes fiscais não protegidas**: `Faixa`/`Tributo` não impedem faixas sobrepostas/lacunas na tabela regressiva — corretude depende do seed correto.
- **Redundância em `Titulo`**: `indexador`/`paga_juros_semestrais` derivados **e** persistidos; import parcial pode divergir do que o ctor derivaria, sem check de consistência.
- **Índices ausentes para filtros comuns** ✔️ RESOLVIDO (tarefa 12, 2026-07-23): filtro por `data_vencimento` isolado (vencido) varria tabela e `GetByNomeAsync` usava expressão não-sargável (`UPPER(... || EXTRACT(YEAR...))`). **Corrigido:** migration `AddTituloIndexes` — `ix_titulos_data_vencimento` (btree, modelado no EF) + `ix_titulos_nome_upper` (índice funcional via `migrationBuilder.Sql`, expressão idêntica à de `GetByNomeAsync`). EXPLAIN no banco real migrado (402 títulos): `GetByNomeAsync` → Index Scan (22ms→0.08ms em lab de 50k); `GetFilteredAsync vencido=true` (14%) → Index Scan; `vencido=false` (86%) → Seq Scan (escolha correta do planner). **Índice em `indexador` avaliado e REJEITADO**: 4 valores distintos, seletividade ~25%, ganho de só 2× em 50k linhas — não justifica o custo de escrita.
- **Desvio CQRS**: `TributoReadRepository` devolve entidade em vez de DTO (contraria a convenção do projeto).

---

## 3. Integrações Externas

### O que existe (4 integrações, registradas em `Infrastructure/DependencyInjection.cs`)

1. **Projeções / BCB Focus** — `FocusBcbService` (`IProjecaoMercadoService`). API Olinda do BCB (OData), `FocusBcb:BaseUrl`. Dois endpoints: `ExpectativasMercadoSelic` e `ExpectativasMercadoInflacao12Meses` (`$filter=Indicador eq '...'`, mapeia IGPM→`IGP-M`). `$top=1&$orderby=Data desc`. Typed client, timeout **30s**. `Prefixado` rejeitado antes de chamar.
2. **Feriados / ANBIMA** — `FeriadoImportService` (`IFeriadoImportService`, `IAsyncEnumerable`). XLS binário (BIFF) via `ExcelDataReader`, `FeriadoImport:Url`. Col 0 = data, col 2 = descrição; baixa o arquivo inteiro para `MemoryStream`. Typed client, timeout **5min**. Agendado por **Quartz** desde a tarefa 10 (`FeriadoImportJob`, cron `Feriados:CronSchedule`, fallback `0 0 6 1 12 ?`, `[DisallowConcurrentExecution]`), além do endpoint manual `POST /importacao/feriados`.
3. **CSV Tesouro Direto** — `CsvImportService` (`ICsvImportService`, `IAsyncEnumerable<Result<CsvRecord>>`). Tesouro Transparente/CKAN, `CsvImport:Url`. CSV `;`, 8 colunas, decimais `pt-BR`, `dd/MM/yyyy`. `StripTrailingYear` remove ano do nome via regex. Streaming linha-a-linha. Typed client, timeout **10min**. Agendado por **Quartz** (cron `0 0 6 * * ?`, `[DisallowConcurrentExecution]`).
4. **Caching** — decorators cache-aside com `IMemoryCache`: `CachedTituloReadRepository` (24h), `CachedPrecoTaxaReadRepository` (6h), `CachedTributoReadRepository` (24h), `CachedFeriadoReadRepository` (7d) e, desde a tarefa 11 (2026-07-23), `CachedProjecaoMercadoService` — por indexador, entrada "fresh" (TTL 6h, `FocusBcb:CacheTtl`) e entrada "lkg"/last-known-good (7 dias, `FocusBcb:MaxFallbackAge`); fallback só em `Projecao.HttpError`, nunca silencioso (`LogWarning` + campo `Origem`=`Bcb`/`CacheFallback`, `ObtidaEmUtc` original preservado). Invalidação dos 4 primeiros disparada pelo `CacheInvalidationBehavior` (pipeline MediatR, `switch` por tipo de comando) via `MemoryCacheInvalidator`. O 5º token (`GetProjecoesToken`/`InvalidateProjecoes`) existe no invalidator e é usado pelo `AddExpirationToken` do `CachedProjecaoMercadoService`, mas **nenhum comando o invalida hoje** — `InvalidateProjecoes()` só é chamado em `ApiTestFactory.cs` (reset entre testes); em produção a expiração das entradas de projeção depende só do TTL/absoluto (fresh 6h / lkg 7d).

### Como se conecta

- **Ingestão:** CSV (Quartz 06:00) → `ImportCsvCommand` → handler grava `Titulo`/`PrecoTaxa` em lotes de 1000, deduplica por `DataBase`; sucesso invalida cache titulos+precos. Feriados (Quartz 1º/dez 06:00, `FeriadoImportJob`, tarefa 10, ou `POST /importacao/feriados` manual) → `ImportFeriadosCommand` → handler grava, deduplica; invalida cache feriados.
- **Consumo:** `SimularCommandHandler` chama `IProjecaoMercadoService.GetProjecaoAsync` (quando `ProjecaoAnual` ausente e não-Prefixado), usa `MedianaAnual`; desde a tarefa 11 (2026-07-23) essa chamada resolve para `CachedProjecaoMercadoService` (cache fresh 6h + fallback last-known-good 7 dias por indexador, `FocusBcbService` como colaborador interno) em vez de bater ao vivo no BCB a cada simulação; usa `IDiasUteisService` → `FeriadoReadRepository` (cacheado) → `DiasUteisCalculator`.
- **Camadas:** ports em Application, adapters em Infrastructure, cálculo puro em Domain. Cache é infra pura via pipeline behavior.

### Fragilidades
- **Resiliência ausente** ✅: nenhum typed client tem Polly/retry/circuit breaker — só timeout. Indisponibilidade transitória de BCB/ANBIMA/Tesouro falha a operação inteira.
- **BCB Focus sem cache e sem fallback** ✔️ RESOLVIDO (tarefa 11, 2026-07-23): Era: `IProjecaoMercadoService` **não** era embrulhado por cache — cada simulação sem `ProjecaoAnual` era 1 chamada ao vivo; BCB fora → simulação inteira falhava; acoplava disponibilidade do simulador à do BCB e não escalava (N simulações = N chamadas). **Corrigido:** `CachedProjecaoMercadoService` decora `IProjecaoMercadoService` — entrada "fresh" por indexador (TTL 6h) evita bater no BCB a cada simulação; entrada "lkg" (7 dias) serve de fallback só quando o BCB devolve `Projecao.HttpError`, nunca silencioso (log + campo `Origem` na resposta). Limitação aceita: `lkg` em memória, zera em restart/deploy.
- **Parsing frágil**: `DateOnly.Parse(entry.Data)` sem cultura explícita; indicador de inflação por igualdade de enum (novo indexador → `$filter` vazio, NotFound silencioso); `FeriadoImportService` depende de posições fixas de coluna e formato BIFF (migração para `.xlsx` quebra); `CsvParserHelper` exige 8 colunas/`pt-BR` fixos, `StripTrailingYear` mutila título terminando em 4 dígitos.
- **Robustez de rede/memória**: XLS inteiro em `MemoryStream` sem limite; exceção durante streaming do corpo (após headers OK) não capturada; desserialização do BCB sem try/catch (JSON malformado → `JsonException` crua).
- **Cache em processo (não distribuído)**: em deploy multi-instância a invalidação por import só limpa a instância que processou; outras servem dados velhos até o TTL. Invalidação depende do `switch` manual no behavior — novo comando de escrita não adicionado deixa cache stale sem teste de guarda.

---

## 4. Jobs / Crons, Startup e Observabilidade

### O que existe

**Jobs (Quartz.NET):** dois jobs registrados. `CsvImportJob` — cron `0 0 6 * * ?` (06:00 diário, configurável), `[DisallowConcurrentExecution]`, `WaitForJobsToComplete = true`. Dispara `ImportCsvCommand` e loga resultado. `FeriadoImportJob` ✔️ RESOLVIDO (tarefa 10, 2026-07-23) — cron `0 0 6 1 12 ?` (06:00 do dia 1º de dezembro, configurável em `Feriados:CronSchedule`), também `[DisallowConcurrentExecution]`, espelha o `CsvImportJob` linha a linha e dispara `ImportFeriadosCommand` (idempotente por dedup de datas). BCB Focus é sob demanda (com cache/fallback — ver §3, tarefa 11).

**Startup** (`API/Program.cs`): `ApiKeyGuard.Validate` aborta o boot em prod se `ApiKey:Key` for vazia ou uma chave proibida (`CHANGE-ME-IN-PRODUCTION` **ou `dev-local-key`** desde a tarefa 19; `Trim`+`OrdinalIgnoreCase`); o `docker-compose.yml` usa `${API_KEY:?}` (sem fallback inseguro). Migrations automáticas em todo ambiente exceto `Testing`. **Healthcheck real:** `AddDbContextCheck<AppDbContext>()` com `/health` + `/health/ready` (readiness, 503 se banco fora) e `/health/live` (liveness, não toca DB). **Seed no boot** (tarefa 9): `InitializeDatabaseAsync` (`API/Extensions/`) migra → semeia tributos IOF/IR via `SeedTributosCommand` idempotente (fonte `TributosPadrao`, validada pelo domínio; FATAL em falha) → importa feriados da ANBIMA só no 1º boot se a tabela estiver vazia (não-fatal). Guard de `Testing` (early-return).

**Observabilidade:**
- **Logs:** Serilog com **CompactJsonFormatter (CLEF) no Console E no sink `GrafanaLoki`** (tarefa O1 + formatter padronizado na tarefa 20), label `job=tesouro-direto-api` e enrichers `service`/`environment`/`MachineName` fixados no código (`API/Extensions/SerilogExtensions.cs`), enrich `FromLogContext`, URI Loki em `Loki:Uri`. Captura de falhas de negócio (sem exceção) via `LoggingBehavior` (MediatR, tarefa O3, em `Application/Common/Behaviors/`) → Warnings estruturados. O campo `level` (`info`/`warning`/`error`) é injetado pelo próprio sink Loki. (O diretório `Infrastructure/Observability` citado na tarefa **não existe**.)
- **Métricas:** `prometheus-net` (`UseHttpMetrics` + `/metrics`) — só métricas HTTP genéricas, **nenhuma de negócio/job** (tarefas 14/O6/O7/O8 pendentes). `prometheus.yml` scrape `app:8080`.
- **Grafana:** provisionado em `infra/grafana/` (datasources UID fixo + dashboard; painéis "Logs by Level" corrigidos na tarefa 20 para casar `level=info/warning/error`; painel "Trace by CorrelationId" via `derivedField`). **Loki:** filesystem, retention 30d, compactor com `delete_request_store`. Alertas provisionados: **pendentes (tarefa O9)**.
- **Web (Blazor):** ✔️ RESOLVIDO (tarefa 17/O2) — Serilog + sink Loki (CLEF, `job=tesouro-direto-web`) + `CorrelationIdHandler` (`DelegatingHandler`) propagando `X-Correlation-Id` à API → trace Web↔API no Grafana. Métricas do Web ainda ausentes.

**Deploy** (`.github/workflows/deploy.yml`): `test` → `e2e` → `deploy` (SSH VPS). `.env` sempre reescrito com `printf`, `git pull`, copia nginx conf, `nginx -t && reload`, `docker compose build --no-cache && up -d`, aguarda `/health`. Migrations rodam no startup do container. Nginx porta 3080: `/`→Web, `/api/`→API, `/grafana/` público, `/prometheus/` restrito a 127.0.0.1.

### Como se conecta

Quartz → `ImportCsvCommand` → handler → HTTP Tesouro + write repos (EF). CorrelationId flui header → LogContext → Loki → derivedField no Grafana. Prometheus scrape `app:8080` → Grafana (UIDs fixos).

### Fragilidades
- **Sem seed de tributos (IOF/IR) e feriados em produção** ✔️ RESOLVIDO (tarefa 9, 2026-07-22): `InitializeDatabaseAsync` (`API/Extensions/DatabaseInitializerExtensions.cs`) no boot semeia tributos (idempotente, via domínio, FATAL em falha) e importa feriados da ANBIMA no 1º boot (não-fatal). Verificado ao vivo: banco novo → `POST /simulador` 200; revisão fiscal manual dos valores semeados OK (2026-07-23). Bug de cache stale (24h) achado e corrigido no caminho. Refresh contínuo de feriados entregue na tarefa 10 (`FeriadoImportJob`, ver Jobs acima). **Follow-ups da 9:** cobertura automatizada da semântica fatal/não-fatal do boot → **tarefa 21**; corrida de dois pods no boot (`DbUpdateException` crua) → **fora de escopo** (efeito final correto; exigiria lock distribuído).
- **Feriados sem agendamento** ✔️ RESOLVIDO (tarefa 10, 2026-07-23): Era: só endpoint manual, sem Quartz — feriados do próximo ano não entrariam sozinhos → distorceria dias úteis → distorceria todas as projeções do Simulador. **Corrigido:** `FeriadoImportJob` registrado no Quartz (`JobKey("feriado-import")`, cron `Feriados:CronSchedule`, fallback `0 0 6 1 12 ?`), dispara `ImportFeriadosCommand`. **Achado do revisor:** a fonte ANBIMA (`feriados_nacionais.xls`) já publica feriados de 2001 até **2099** de uma vez — o boot da tarefa 9 já importa todos os anos futuros. O valor real deste job, portanto, **não** é "esperar o próximo ano ser publicado" (a premissa original acima), e sim resiliência a correções/adições pontuais da fonte ao longo do tempo.
- **Lock apenas intra-instância** ⚠️: `[DisallowConcurrentExecution]` + RAMJobStore (in-memory). Multi-instância ou cron concomitante a `POST /importacao` manual → sem lock distribuído. Idempotência real vem de `existingDates.Contains(DataBase)`, mas `GetOrCreateTitulo` não é atômico — depende da constraint única `ix_titulos_tipo_vencimento` (ver Verificação #6).
- **Job silencioso em falha** ✅: `CsvImportJob` só faz `LogError`; sem métrica, sem alerta, sem relançar. Fonte fora do ar por dias passa despercebida.
- **Sem métricas de negócio/job** ✅: nenhum counter/gauge para sucesso/falha do import, linhas processadas, idade do último preço, duração do job. Impossível alertar em Grafana.
- **Healthcheck raso** ✔️ RESOLVIDO (tarefa 3, 2026-07-19): `AddDbContextCheck` + `/health`/`/health/ready` (503 com banco fora) e `/health/live` (liveness); Docker healthcheck e gates de deploy apontam para `/health/ready`.
- **Web sem observabilidade** ✔️ RESOLVIDO (tarefa 17/O2, 2026-07-21): Serilog+Loki no Web + `CorrelationIdHandler` → logs e CorrelationId da UI entram no Loki/Grafana (trace Web↔API). Resíduo: métricas do Web (latência/erro de render) ainda ausentes.
- **Grafana exposto publicamente com senha default** ✔️ RESOLVIDO (tarefa 1 + commit `ba3b103`, 2026-07-19): `GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:?...}` (boot falha sem o secret, sem fallback `:-admin`) e `/grafana/` restrito por IP no nginx.

---

## 5. Testes

### O que existe

6 projetos C# (**~296 métodos** xUnit) + E2E Playwright/TS (**22 testes**). Stack: xUnit 2.5.3, FluentAssertions 8.9, NSubstitute 5.3, coverlet; Testcontainers.PostgreSql + Mvc.Testing (só API.Tests).

- **Domain.Tests** — unitário puro: VOs, entidades, Result, `DiasUteisCalculator` (14), `SimuladorService` (16, maior concentração), `TipoTitulo` pattern matching (17).
- **Application.Tests** — handlers CQRS com repos mockados (NSubstitute): PrecosTaxas, Titulos, Tributos, Simulador, Importacao, Feriados.
- **API.Tests** — mistura: `Persistence/` (5 classes, **integração real Testcontainers postgres:16**, migrate + write/read repos); `Middleware/` (3 classes, `WebApplicationFactory` + connection string fake — ApiKey 7 testes, CorrelationId, métricas); `Projecoes/FocusBcbServiceTests` (HTTP mockado); `Feriados/FeriadoImportServiceTests` (.xls real embutido); `CsvImport/` (parser + job).
- **Infrastructure.Tests** — cobre **apenas** `Caching/` (decorators, invalidator, behavior).
- **Architecture.Tests** — impõe direção de dependências, convenções de código (handlers ctor único, commands records, Application sealed, repos retornam `Task<Result>`) e convenções de Domain (sealed, sem `List<>` exposto).
- **E2E.Tests** — Playwright: projeto `api` (health/metrics) + `web` (5 páginas), `retries: 2`, `workers: 1`, helpers com espera de `window.Blazor`.

### Como se conecta

**CI gateia deploy:** `test` (dotnet test + cobertura opencover + SonarQube condicional; tem service container postgres 5432 **aparentemente não consumido**) → `e2e` (`docker-compose.e2e.yml` + seed.sql via psql + Playwright chromium; E2E vermelho bloqueia deploy) → `deploy` (SSH). Ambiente E2E efêmero (db postgres:16 + app + web). `seed.sql` idempotente (TRUNCATE+INSERT): 7 títulos, preços, IOF+IR, feriados 2024-2025 — independente das migrations. Testes de integração de Infrastructure moram fisicamente em **API.Tests**, não em Infrastructure.Tests.

### Fragilidades
- **Zero teste de integração HTTP nas 11 rotas de negócio** ✅ (Verificação #10): `WebApplicationFactory` só nos 3 testes de middleware (batem em `/` e `/health`). Rotas reais cobertas só por handlers unitários (repos mockados) + E2E via Blazor. Bug de roteamento/serialização (ex.: enums numéricos) passaria despercebido.
- **E2E web baseado em presença de elementos**: muitos testes só `toBeVisible()`/contagem (contraria diretriz de testes comportamentais). Há exceções boas (simulador valida `R$`, tributos cria real, titulos filtra).
- **Flakiness inerente do Blazor Server**: `retries: 2` + hacks `waitForTimeout(1000)` "if Blazor missed the event".
- **Adapters de I/O sem integração real**: FocusBcb só com `FakeHttpMessageHandler`; `CsvImportService` **sem teste dedicado** (só mockado no handler).
- **Infrastructure.Tests só cobre Caching**; resto testado a partir de API.Tests (placement inconsistente).
- **Sem gate de cobertura** (coleta mas não reprova build); **sem testes de componente Blazor (bUnit)**.
- **Drift de schema seed↔migrations**: `seed.sql` define schema manualmente em paralelo às migrations — mudança numa migration sem atualizar seed quebra E2E silenciosamente.

---

## Verificação das fragilidades

Passo adversarial: o revisor tentou **refutar** cada fragilidade de maior impacto lendo o código-fonte real (não docs/comentários). **Nenhuma das 10 foi refutada** — todas confirmadas literalmente, com uma nuance agravante no item 8. **Status de correção** (coluna final, atualizado 2026-07-23): itens **1, 2, 3, 4, 5, 7, 8, 9 e 10 resolvidos** (tarefas 4, 2, 11, 9, 10, 3, 1, 7 e 8 do PLANO); segue **aberto** apenas o item **6** (upsert não-atômico).

| # | Afirmação | Veredito | Evidência |
|---|-----------|----------|-----------|
| 1 | Sem `JsonStringEnumConverter` → enums só aceitam número no POST | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 4) | Era: zero ocorrência de `JsonStringEnumConverter` em `src`. **Corrigido:** `JsonStringEnumConverter` registrado (aceita string E número); binding agora via `CreateTributoRequest` (tarefa 15). |
| 2 | `Indexador.FromName(v).Value` quebra materialização EF com valor fora da whitelist | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 2) | `Indexador.cs:11-24` (whitelist 4); `Result<T>.Value` lança em falha (`Common/Result.cs:47-48`); usado em `TituloConfiguration.cs:36`. **Corrigido:** `Indexador.FromPersistence` (lossless) na leitura EF. |
| 3 | Simulador chama BCB Focus ao vivo, sem cache/fallback | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 11) | Era: `SimularCommandHandler.cs:40-47` propagava falha sem fallback; `DependencyInjection.cs:82-85` registrava sem decorator `Cached*` (contraste com repos, linhas 40-73). **Corrigido:** `CachedProjecaoMercadoService` (`Infrastructure/Projecoes/`) registrado como `IProjecaoMercadoService` em `DependencyInjection.cs` (com `FocusBcbService` como colaborador interno); cache fresh 6h + fallback last-known-good 7 dias por indexador, fallback só em `Projecao.HttpError`. |
| 4 | Nenhum seed de tributos/feriados em produção | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 9) | Era: zero `.HasData(` em `src`, único `seed.sql` de teste. **Corrigido:** `InitializeDatabaseAsync` semeia tributos (via `SeedTributosCommand` idempotente) + importa feriados no 1º boot; verificado ao vivo em banco novo. |
| 5 | Import de feriados sem job Quartz (só endpoint manual) | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 10) | Era: `DependencyInjection.cs:87-96` só registrava `JobKey("csv-import")`; sem `FeriadoImportJob`; caminho único era `ImportacaoEndpoints.cs:19-25`. **Corrigido:** novo `FeriadoImportJob.cs` + `JobKey("feriado-import")`/`"feriado-import-trigger"` registrado em `DependencyInjection.cs` (cron `Feriados:CronSchedule`, fallback `0 0 6 1 12 ?`), disparando `ImportFeriadosCommand`. |
| 6 | `ix_titulos_tipo_vencimento` é UNIQUE e o upsert não é atômico | ✅ CONFIRMADO | `TituloConfiguration.cs:42-44` `.IsUnique()`; `GetOrCreateTituloAsync` (`ImportCsvCommandHandler.cs:107-132`) é check-then-act sem transação → race sob concorrência (UNIQUE evita duplicata mas lança exceção não tratada). |
| 7 | Healthcheck raso (`/health` string fixa, não toca banco) | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 3) | `Program.cs:29`; zero `AddHealthChecks`/`AddDbContextCheck`/`MapHealthChecks` em `src`. **Corrigido:** `AddDbContextCheck` + readiness/liveness. |
| 8 | Grafana público + senha default `admin` | ✅ CONFIRMADO (agravado) → ✔️ RESOLVIDO (tarefa 1) | `nginx/tesouro-direto.conf` `location /grafana/` sem `allow/deny` (vs `/prometheus/` que tem `allow 127.0.0.1; deny all`); `docker-compose.yml:67` `${GRAFANA_PASSWORD:-admin}`. **Exposição pública foi decisão recente e intencional** (commit `67406be`). **Corrigido:** `GRAFANA_PASSWORD:?` (sem fallback) + `/grafana/` restrito por IP (commit `ba3b103`). |
| 9 | PUT tributos decide 404 vs 400 por `Error.Code.Contains("NotFound")` | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 7) | Era: substring match em `ConfiguracaoEndpoints.cs:33`. **Corrigido:** helper `ToHttpResult` mapeia por estrutura (`.EndsWith(".NotFound")`→404), aplicado nos 4 endpoints. |
| 10 | Zero teste de integração HTTP nas rotas de negócio | ✅ CONFIRMADO → ✔️ RESOLVIDO (tarefa 8) | Era: `WebApplicationFactory` só em 3 testes de middleware. **Corrigido:** suíte `Integration/` (Testcontainers) cobre Titulos/Simulador/Tributos/Importacao/Auth/Health; bug real de prod (Dapper/`DateOnly`) pego no caminho. |

**Conclusão do revisor:** 10/10 confirmadas contra o código real. As demais fragilidades listadas nas seções §1–§5 (sem verificação individual marcada) vêm da leitura dos agentes de mapeamento e são consistentes com o código, mas não passaram pelo passo adversarial explícito.

---

## Fragilidades priorizadas (síntese)

**Bloqueiam operação / corretude:**
1. **Sem seed versionado de tributos e feriados** ✔️ RESOLVIDO (tarefa 9): seed no boot (tributos idempotente + feriados no 1º boot) → Simulador funciona em banco novo (§4).
2. ✔️ RESOLVIDO (tarefa 10) **Feriados sem job agendado** → `FeriadoImportJob` (Quartz) faz o refresh contínuo (§4).
3. ✔️ RESOLVIDO (tarefa 11) **BCB Focus ao vivo, sem cache nem fallback** → `CachedProjecaoMercadoService` desacopla o simulador da disponibilidade do BCB (§3).
4. ✔️ RESOLVIDO (tarefa 2) **`Indexador` whitelist rígida** → materialização EF quebra com valor inesperado na coluna (§2).

**Segurança / operação:**
5. Chave de API única compartilhada, default `CHANGE-ME-IN-PRODUCTION`; endpoints mutantes com mesma proteção que leitura (§1). **Parcial** ✔️ (tarefas 5 + 19): boot aborta em prod com chave default/vazia **ou `dev-local-key`**, e o compose falha sem `API_KEY` (sem fallback inseguro); segue aberto o compartilhamento único e a paridade leitura/escrita.
6. ✔️ RESOLVIDO (tarefa 1) Grafana público com senha default `admin` (§4).
7. Sem retry/circuit breaker em nenhuma integração externa (§3).
8. ✔️ RESOLVIDO (tarefa 3) Healthcheck raso não detecta banco fora (§4).

**Qualidade / manutenção:**
9. Contrato HTTP vazando `CreateTributoCommand` ✔️ RESOLVIDO (tarefa 15); acesso à API duplicado em 5 páginas Blazor segue aberto (tarefa 16) (§1).
10. Enums só numéricos + `ParseEnum` hardcoded frágil ✔️ RESOLVIDO (tarefa 4) (§1).
11. Zero teste de integração de endpoint HTTP ✔️ RESOLVIDO (tarefa 8); gate de cobertura segue aberto (tarefa 18) (§5).
12. Observabilidade sem métricas de negócio/job (aberto, tarefas 14/O6–O8); Web no Loki/Grafana ✔️ RESOLVIDO (tarefa 17/O2); logs padronizados e painéis de nível corrigidos (tarefas O1/O3/20) (§4).
