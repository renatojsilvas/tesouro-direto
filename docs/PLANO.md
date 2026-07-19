# Plano de Melhorias — Tesouro Direto API

> Base: [`docs/MAPA.md`](./MAPA.md) e sua seção de Verificação (10 fragilidades confirmadas contra o código).
> Tarefas **pequenas e independentes**: cada uma pode ser feita e mergeada sozinha.
> Ordenadas por **risco/retorno** — melhor relação (alto retorno, baixo risco) primeiro.

## Como ler

Cada tarefa tem: **Escopo** (o que fazer) · **Arquivos** · **Risco** (o que pode quebrar) · **Verificação** (como saber que ficou pronta). Estimativas de esforço: 🟢 pequeno (<½ dia) · 🟡 médio (½–1 dia) · 🔴 grande (>1 dia).

## Ordem sugerida (risco × retorno)

| # | Tarefa | Retorno | Risco | Esforço |
|---|--------|---------|-------|---------|
| 1 | ✅ Grafana: falhar sem senha (remover `:-admin`) — concluída 2026-07-19 | Alto (segurança) | Baixo | 🟢 |
| 2 | `Indexador` por pattern matching (não quebrar EF) | Alto (corretude) | Baixo | 🟢 |
| 3 | Healthcheck real com DB check | Alto (operação) | Baixo | 🟢 |
| 4 | Enums como string no JSON (`JsonStringEnumConverter`) | Alto (API/Web) | Baixo | 🟢 |
| 5 | API key: falhar em prod se for o default | Alto (segurança) | Baixo | 🟢 |
| 6 | Exception handler global (ProblemDetails) na API | Médio | Baixo | 🟢 |
| 7 | Helper `Result`→HTTP (fim do `Contains("NotFound")`) | Médio | Baixo | 🟢 |
| 8 | Testes de integração HTTP das rotas | Alto (rede de segurança) | Baixo | 🟡 |
| 9 | Seed versionado de tributos e feriados | Muito alto (corretude) | Médio | 🟡 |
| 10 | Job Quartz de feriados | Alto (corretude) | Baixo | 🟢 |
| 11 | BCB Focus: cache + fallback | Alto (disponibilidade) | Médio | 🟡 |
| 12 | Índices para filtros comuns | Médio (performance) | Baixo | 🟢 |
| 13 | Retry/circuit breaker (Polly) nas integrações | Médio (resiliência) | Médio | 🟡 |
| 14 | Métricas de negócio/job no Prometheus | Médio (observabilidade) | Baixo | 🟡 |
| 15 | Separar contrato HTTP do `CreateTributoCommand` | Médio (arquitetura) | Baixo | 🟢 |
| 16 | Cliente tipado no Web (dedup das 5 páginas) | Médio (manutenção) | Médio | 🔴 |
| 17 | Observabilidade no Web (Serilog/Loki) | Médio | Baixo | 🟢 |
| 18 | Gate de cobertura no CI | Baixo–Médio | Baixo | 🟢 |

---

## Onda 1 — Quick wins (baixo risco, alto retorno)

### 1. Grafana: falhar sem senha explícita 🟢 ✅ Concluída (2026-07-19)
> **Feito:** `GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:?...}` (sintaxe `:?` — falha o boot se ausente **ou vazia**). Revisor confirmou as 4 verificações do PLANO e não achou furo (sem `.env`/override conflitante, sem provisioning alternativo, sem auth anônima). `deploy.yml` já injetava o secret. Risco remanescente: se o secret `GRAFANA_PASSWORD` do GitHub estiver vazio, o deploy falha (comportamento correto) — não verificável localmente.
- **Escopo:** remover o fallback `:-admin` de `GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:-admin}` para que o container **não suba** sem `GRAFANA_PASSWORD` no `.env`. (A restrição de rede no nginx já foi feita no commit `ba3b103`.)
- **Arquivos:** `docker-compose.yml`. Garantir `GRAFANA_PASSWORD` no `.env` do VPS e no passo `printf` do `.github/workflows/deploy.yml`.
- **Risco:** se a env não estiver setada, o Grafana deixa de subir — pode derrubar observabilidade num deploy. Mitigar setando o secret **antes** de mergear.
- **Verificação:** `docker compose config` sem `GRAFANA_PASSWORD` → erro/variável vazia explícita; com a env setada, Grafana sobe e login com `admin/admin` falha.

### 2. `Indexador` derivado por pattern matching 🟢
- **Escopo:** eliminar a quebra de materialização EF quando a coluna `indexador` tem valor fora da whitelist. Aplicar o mesmo padrão já usado em `TipoTitulo.DeriveIndexador` (derivação sem falhar) ou tornar a conversão de leitura tolerante (fallback em vez de `.Value` sobre `Result` de falha).
- **Arquivos:** `src/TesouroDireto.Domain/Titulos/Indexador.cs`, `src/TesouroDireto.Infrastructure/Persistence/Configurations/TituloConfiguration.cs`.
- **Risco:** mudar a semântica de `Indexador` pode afetar filtros por indexador e testes de Domain que esperam `Error` para nomes inválidos. Ajustar testes junto.
- **Verificação:** teste novo — inserir linha em `titulos` com `indexador` fora da whitelist e materializar via read repo EF **sem lançar**; suíte de Domain (`TipoTituloTests`/novos) verde. Ver memória `feedback_vo_whitelist_fragile`.

### 3. Healthcheck real com DB check 🟢
> Detalhado no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), item **O5** (inclui separação liveness/readiness).
- **Escopo:** trocar `MapGet("/health", () => "healthy")` por `AddHealthChecks().AddDbContextCheck<AppDbContext>()` + `MapHealthChecks("/health")`. Manter isento de ApiKey.
- **Arquivos:** `src/TesouroDireto.API/Program.cs` (e `Extensions/` se extrair).
- **Risco:** healthcheck do Docker/deploy passa a depender do banco — se o Postgres demorar a subir, o gate `/health` do deploy pode falhar (comportamento correto, mas checar o timeout do compose). Manter startup order/`depends_on`.
- **Verificação:** `/health` retorna 200 com banco OK e **503** com o Postgres parado (`docker compose stop db && curl -i /health`).

### 4. Enums como string no JSON 🟢
- **Escopo:** registrar `JsonStringEnumConverter` (via `ConfigureHttpJsonOptions`) na API para aceitar/serializar `BaseCalculo`/`TipoCalculo` como string; remover o `ParseEnum` hardcoded do Web.
- **Arquivos:** `src/TesouroDireto.API/Program.cs`; `src/TesouroDireto.Web/Components/Pages/Tributos.razor` (remover `ParseEnum`, enviar string).
- **Risco:** clientes que hoje mandam **número** no `POST /configuracoes/tributos` quebram (mas o único cliente é o próprio Web). `JsonStringEnumConverter` aceita ambos na desserialização por padrão? Não — confirmar; se preciso, manter compat. Ajustar E2E de tributos.
- **Verificação:** `POST /configuracoes/tributos` com `"baseCalculo":"Rendimento"` retorna 201; E2E `tributos.spec.ts` verde. Ver memória `feedback_api_enums_numeric`.

### 5. API key: falhar em produção se for o default 🟢
- **Escopo:** no startup, se `ASPNETCORE_ENVIRONMENT != Development/Testing` e `ApiKey:Key == "CHANGE-ME-IN-PRODUCTION"` (ou vazia), lançar e abortar o boot.
- **Arquivos:** `src/TesouroDireto.API/Program.cs` ou `Middleware/ApiKeyMiddleware.cs` (validação no registro).
- **Risco:** se o `.env` de prod não tiver `ApiKey__Key`, a API deixa de subir. Garantir o secret antes de mergear.
- **Verificação:** subir com env de prod e chave default → falha explícita no log; com chave real → sobe normal.

### 6. Exception handler global (ProblemDetails) na API 🟢
> Também coberto no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), item **O4** (captura de erros, camada 1).
- **Escopo:** adicionar `UseExceptionHandler` + `AddProblemDetails` para que exceções não tratadas virem resposta `application/problem+json` consistente (hoje viram 500 cru). Padronizar também o corpo do 401 do `ApiKeyMiddleware`.
- **Arquivos:** `src/TesouroDireto.API/Program.cs`, `src/TesouroDireto.API/Middleware/ApiKeyMiddleware.cs`.
- **Risco:** baixo; muda o formato do corpo de erro 500 (nenhum cliente depende do corpo cru hoje).
- **Verificação:** endpoint que lança exceção retorna `problem+json` com `traceId`/CorrelationId; 401 passa a ter corpo.

### 7. Helper `Result`→HTTP compartilhado 🟢
- **Escopo:** extrair um helper (`results.ToHttp()` / `Match`) que mapeia `Error.Code` → status **por tipo/estrutura do erro**, eliminando o `Error.Code.Contains("NotFound")` do PUT e o boilerplate repetido em cada endpoint.
- **Arquivos:** novo `src/TesouroDireto.API/Extensions/ResultExtensions.cs`; `Endpoints/*.cs` (todos).
- **Risco:** mudar o mapeamento pode alterar status codes de alguns endpoints — cobrir com os testes de integração da tarefa 8 antes/junto.
- **Verificação:** `PUT /configuracoes/tributos/{id}` inexistente → 404; erro de validação → 400; testes de integração confirmam status por rota.

---

## Onda 2 — Rede de segurança e correções de fundo

### 8. Testes de integração HTTP das rotas 🟡
- **Escopo:** com `WebApplicationFactory<Program>` (env `Testing`, banco via Testcontainers ou connection real), exercitar as 11 rotas de negócio: status, serialização (inclui enums), binding, contrato de erro. É a rede de segurança para as tarefas 4, 7 e refactors.
- **Arquivos:** novo `tests/TesouroDireto.API.Tests/Endpoints/*` (seguir o padrão de `Persistence/` com `IAsyncLifetime`).
- **Risco:** baixo (só adiciona testes). Custo de infra: sobe container Postgres — usar fixture compartilhada (`ICollectionFixture`) para não subir um por classe (ver fragilidade §5.7 do MAPA).
- **Verificação:** `dotnet test` cobre GET/POST/PUT de cada rota; falha se roteamento/serialização quebrar. Ver memórias `feedback_webappfactory_needs_db_config`, `feedback_integration_tests_for_adapters`.

### 9. Seed versionado de tributos e feriados 🟡
- **Escopo:** tornar reproduzível o estado que hoje é manual. Opções: migration `HasData` para tributos (IOF/IR conforme lei) **ou** script SQL idempotente de produção versionado + passo de aplicação no deploy. Feriados: rodar a importação no primeiro boot (ver tarefa 10) ou seed inicial.
- **Arquivos:** `src/TesouroDireto.Infrastructure/Persistence/Migrations/` (nova migration) ou novo `infra/seed/*.sql` + `.github/workflows/deploy.yml`.
- **Risco:** **médio** — mexer em dados fiscais; um seed errado corrompe cálculo do Simulador. Basear-se em `project_tributos_configurados` (valores atuais) e validar contra a lei. Garantir idempotência (não duplicar em banco já populado).
- **Verificação:** subir banco novo (`docker compose down -v && up`), sem intervenção manual, e `POST /simulador` retorna resultado válido (não erro de tributo/feriado ausente). Ver memórias `project_tributos_configurados`, `feedback_postgres_volume_password`.

### 10. Job Quartz de feriados 🟢
- **Escopo:** criar `FeriadoImportJob` (espelhando `CsvImportJob`) disparando `ImportFeriadosCommand` num cron (ex.: mensal/anual). `[DisallowConcurrentExecution]`.
- **Arquivos:** novo `src/TesouroDireto.Infrastructure/Feriados/FeriadoImportJob.cs`; registro em `src/TesouroDireto.Infrastructure/DependencyInjection.cs`; config de cron em `appsettings.json`.
- **Risco:** baixo (import já é idempotente por dedup de datas). Cuidar para não colidir com a ANBIMA fora do ar (a resiliência vem da tarefa 13).
- **Verificação:** teste do job (como `CsvImportJobTests`) e log do disparo agendado; feriados do próximo ano entram sem ação manual.

### 11. BCB Focus: cache + fallback 🟡
- **Escopo:** desacoplar o Simulador da disponibilidade do BCB. (a) Decorator de cache para `IProjecaoMercadoService` (TTL curto, ex.: horas); (b) fallback para última projeção conhecida quando o BCB falhar, em vez de falhar a simulação inteira.
- **Arquivos:** novo `src/TesouroDireto.Infrastructure/Projecoes/CachedProjecaoMercadoService.cs` + registro em `DependencyInjection.cs`; possivelmente ajuste em `src/TesouroDireto.Application/Simulador/SimularCommandHandler.cs`.
- **Risco:** **médio** — fallback pode servir projeção velha silenciosamente; logar/sinalizar quando usar fallback. Não misturar com escrita.
- **Verificação:** teste com `FakeHttpMessageHandler` simulando BCB fora → simulação usa cache/fallback e **não** falha; N simulações = 1 chamada externa dentro do TTL.

### 12. Índices para filtros comuns 🟢
- **Escopo:** migration adicionando índice em `data_vencimento` (filtro "vencido") e avaliar índice em `indexador`; revisar a query não-sargável `GetByNomeAsync` (`UPPER(... || EXTRACT(YEAR...))`) — considerar coluna/índice funcional.
- **Arquivos:** nova migration em `src/TesouroDireto.Infrastructure/Persistence/Migrations/`; possivelmente `Repositories/TituloReadRepository.cs`.
- **Risco:** baixo; migration só adiciona índice. Confirmar impacto de escrita (import) é desprezível no volume atual.
- **Verificação:** `EXPLAIN` das queries de `GetFilteredAsync`/`GetByNomeAsync` usando índice; migration aplica limpo.

### 13. Retry/circuit breaker nas integrações 🟡
- **Escopo:** adicionar Polly (`AddResilienceHandler` / `Microsoft.Extensions.Http.Resilience`) aos 3 typed clients (BCB, ANBIMA, Tesouro): retry com backoff + timeout + circuit breaker.
- **Arquivos:** `src/TesouroDireto.Infrastructure/DependencyInjection.cs` (registro dos `AddHttpClient`).
- **Risco:** **médio** — retry em POST de import pode reprocessar; import já é idempotente por dedup, mas confirmar. Ajustar timeouts para não estourar o do job.
- **Verificação:** teste com handler que falha as primeiras N vezes → cliente re-tenta e sucede; circuit abre após falhas consecutivas.

---

## Onda 3 — Observabilidade, arquitetura e manutenção

### 14. Métricas de negócio/job no Prometheus 🟡
> **Expandido** no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), camada 3 (itens **O6/O7** — os 6 eventos de negócio definidos + alertas). Ver também `docs/analises/observabilidade.md`.
- **Escopo:** expor counters/gauges: sucesso/falha do import, linhas processadas, **idade do último preço importado**, duração do job. Permite alertar em Grafana sobre "import falhou"/"dados velhos".
- **Arquivos:** `src/TesouroDireto.Infrastructure/CsvImport/CsvImportJob.cs`, `ImportCsvCommandHandler.cs`; painel em `infra/grafana/dashboards/`.
- **Risco:** baixo. Cuidar de nomes de métrica estáveis.
- **Verificação:** `/metrics` expõe as novas séries; painel/alerta no Grafana reage a import falho.

### 15. Separar contrato HTTP do `CreateTributoCommand` 🟢
- **Escopo:** introduzir um `CreateTributoRequest` em `API/Contracts/` e mapear para o comando no endpoint, parando o vazamento da camada Application no contrato público.
- **Arquivos:** novo `src/TesouroDireto.API/Contracts/CreateTributoRequest.cs`; `Endpoints/ConfiguracaoEndpoints.cs`.
- **Risco:** baixo; o JSON de entrada não muda se o request espelhar o comando. Cobrir com teste de integração (tarefa 8).
- **Verificação:** `POST /configuracoes/tributos` mantém o mesmo contrato; mudança interna no comando não altera o request.

### 16. Cliente tipado no Web (dedup das 5 páginas) 🔴
- **Escopo:** criar um `TesouroApiClient` em `Web/Services/` encapsulando `CreateClient`, montagem de request, desserialização e `ApiError`; refatorar as 5 páginas para usá-lo.
- **Arquivos:** novo `src/TesouroDireto.Web/Services/TesouroApiClient.cs`; `Components/Pages/{Titulos,Historico,Tributos,Simulador,Cenarios}.razor`.
- **Risco:** **médio** — toca todas as telas; regressão de UI. Fazer incremental (uma página por vez) e apoiar nos E2E.
- **Verificação:** E2E web (todas as specs) verde após o refactor; nenhuma página monta HttpClient/`ApiError` local.

### 17. Observabilidade no Web (Serilog/Loki) 🟢
> Detalhado no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), item **O2** (correlação Web→API via `DelegatingHandler`).
- **Escopo:** configurar Serilog + sink Loki no Web (hoje só logging default), propagando CorrelationId nas chamadas à API para trace end-to-end.
- **Arquivos:** `src/TesouroDireto.Web/Program.cs`; reuso de `SerilogExtensions`.
- **Risco:** baixo. Ver memória `feedback_loki_serilog_labels` (labels no código, não JSON).
- **Verificação:** logs do Web aparecem no Loki com label do serviço; CorrelationId conecta UI→API no Grafana.

### 18. Gate de cobertura no CI 🟢
- **Escopo:** reprovar o build se a cobertura cair abaixo de um limite (via coverlet threshold ou quality gate do SonarQube já existente).
- **Arquivos:** `.github/workflows/deploy.yml` e/ou `sonar-project.properties` / `Directory.Build.props`.
- **Risco:** baixo; pode quebrar o pipeline inicialmente — começar com threshold no nível atual e subir.
- **Verificação:** PR que reduz cobertura abaixo do limite falha o job `test`.

---

## Fora de escopo por ora (registrar, não fazer)

- Versionamento de API (`/v1`) e Swagger/OpenAPI — melhoria de DX, sem risco operacional imediato.
- Rate limiting **no nível da aplicação** (já existe no nginx).
- `VO Taxa` sem invariante e converters `.Value` defensivos (`DataBase`/`PrecoUnitario`) — baixo impacto enquanto os reads passam por Dapper; endereçar se surgir leitura EF desses campos.
- Cache distribuído (Redis) — só necessário se escalar para múltiplas instâncias da API.

---

## Dependências entre tarefas

- **8 (testes de integração)** dá rede de segurança para **4, 7, 15, 16** — fazer antes ou junto.
- **10 (job de feriados)** e **9 (seed)** se complementam: com o job, o "seed" de feriados pode ser o primeiro run agendado.
- **13 (resiliência)** reforça **10 e 11** (ANBIMA/BCB fora do ar).
- Todas as demais são independentes e mergeáveis isoladamente.

---

## Anexo — Observabilidade em 3 camadas

> Base: [`docs/analises/observabilidade.md`](./analises/observabilidade.md). **Stack decidida: nativo incremental** — evoluir Serilog+Loki (logs) e prometheus-net→Prometheus→Grafana (métricas), reusando o padrão de pipeline behavior do MediatR. Rejeitados: `pino` (é Node; projeto é .NET 8) e OpenTelemetry agora (exigiria backend de traces/Tempo, mudança maior). Trade-off aceito: correlação Web→API por header manual, sem trace distribuído.
>
> Ordem: **camada 1 (fundação) → camada 2 (técnica) → camada 3 (negócio)**. Alguns itens expandem/detalham tarefas já listadas acima (3, 6, 14, 17); os demais são novos.

### Camada 1 — Fundação (logs estruturados, correlação, captura de erros)

**O1. Logs em JSON + remover sink Loki duplicado** 🟢
- Escopo: Console com `CompactJsonFormatter`; remover a duplicidade do sink Loki (`appsettings.json:15` **e** `SerilogExtensions.cs:16` → manter só no código); enrichers `environment`/`service`/`MachineName`; validar o `derivedField` do CorrelationId no Grafana.
- Arquivos: `src/TesouroDireto.API/Extensions/SerilogExtensions.cs`, `src/TesouroDireto.API/appsettings.json`, `infra/grafana/provisioning/datasources/datasources.yml`.
- Risco: baixo (formato de log local muda; parsing do Loki melhora). Verificação: Loki mostra linhas JSON com `CorrelationId` e **sem** duplicata.

**O2. Correlação ponta-a-ponta Web→API** 🟢 · *(expande tarefa 17)*
- Escopo: Serilog+Loki no Web + `DelegatingHandler` que injeta `X-Correlation-Id` nas chamadas à API (hoje só manda `X-Api-Key`).
- Arquivos: `src/TesouroDireto.Web/Program.cs`, novo `src/TesouroDireto.Web/Services/CorrelationIdHandler.cs`.
- Risco: baixo (Web passa a depender do Loki no boot — sink não-bloqueante). Verificação: um único `CorrelationId` em Web+API para a mesma ação.

**O3. `LoggingBehavior` (MediatR) + `IResult` para captura de falhas sem exceção** 🟡
- Escopo: behavior espelhando `CacheInvalidationBehavior` que loga início/fim e captura `Result.IsFailure` como Warning (`Error.Code`/`Description`). Introduzir interface mínima `IResult { bool IsSuccess; Error Error; }` em `Result`/`Result<T>` para leitura genérica (evita o pattern-match por tipo que hoje cai no `default`). Logar linhas de CSV inválidas individualmente.
- Arquivos: novo `LoggingBehavior` (Application/Common ou Infrastructure), `src/TesouroDireto.Domain/Common/Result.cs` (+`IResult`, aditivo), registro em `DependencyInjection.cs`, `ImportCsvCommandHandler.cs`.
- Risco: baixo–médio — toca `Result` do Domain; rodar `Architecture.Tests`. Verificação: comando que falha → Warning no Loki, sem 500.

**O4. Exception handler global (ProblemDetails)** 🟢 · *(= tarefa 6)*
- Escopo/arquivos/verificação: ver tarefa 6. Fecha a captura de erros da camada 1 (exceções não tratadas → `problem+json` com CorrelationId; corpo do 401 padronizado).

### Camada 2 — Métricas técnicas (latência, erro, saturação, healthcheck)

**O5. Healthcheck real + liveness/readiness** 🟢 · *(= tarefa 3, expandida)*
- Escopo: `AddDbContextCheck<AppDbContext>()`; separar `/health/live` (sem deps) de `/health/ready` (com DB); apontar Docker healthcheck e gate de deploy para readiness.
- Arquivos: `src/TesouroDireto.API/Program.cs`, `appsettings.json` (ExcludedPaths), `docker-compose.yml`, `.github/workflows/deploy.yml`.
- Risco: baixo–médio (gate passa a exigir DB; conferir `start_period`). Verificação: `stop db` → `/health/ready` 503, `/health/live` 200.

**O6. `MetricsBehavior` (MediatR) — latência/erro por caso de uso** 🟡
- Escopo: histogram de duração + counter de desfecho (`success|failure|exception`) por `request_type`, complementando o `http_request_duration`. Reusa `IResult` (O3).
- Arquivos: novo `src/TesouroDireto.Infrastructure/Observability/MetricsBehavior.cs`, registro em `DependencyInjection.cs`.
- Risco: baixo (cardinalidade controlada). Verificação: `/metrics` expõe `mediatr_request_duration_seconds`/`mediatr_requests_total`.

**O7. Métricas de dependências externas** 🟢
- Escopo: `UseHttpClientMetrics()` nos 3 typed clients (BCB/ANBIMA/Tesouro) → latência/erro por dependência.
- Arquivos: `src/TesouroDireto.Infrastructure/DependencyInjection.cs` (registros `:75/80/85`), `src/TesouroDireto.API/Program.cs`.
- Risco: baixo. Verificação: `httpclient_request_duration_seconds` por client em `/metrics`.

### Camada 3 — Métricas de negócio (medem se o produto funciona)

**O8. Instrumentar os eventos de negócio** 🟡 · *(expande tarefa 14)*
Helper `BusinessMetrics` em `src/TesouroDireto.Infrastructure/Observability/`. 4 essenciais (E1–E4) + 2 complementares:

| Métrica | Tipo | Labels | Onde | Mede |
|---------|------|--------|------|------|
| `import_last_success_timestamp_seconds` | Gauge | — | `ImportCsvCommandHandler` | **frescor do dado** (age = `time()-metric`) |
| `import_runs_total` | Counter | `outcome` | `CsvImportJob` | ingestão roda/passa? |
| `simulations_total` | Counter | `indexador`,`outcome` | `SimularCommandHandler` | caso de uso principal vivo? |
| `simulation_failures_total` | Counter | `reason` | `SimularCommandHandler` | simulador degradado (liga ao BCB) |
| `import_prices_processed_total` | Counter | `kind` | `ImportCsvCommandHandler` | volume/qualidade da ingestão |
| `tributos_config_changes_total` | Counter | `op` | `Create/UpdateTributoCommandHandler` | mudança de config fiscal |

- Risco: baixo — **cuidar de cardinalidade** (nunca IDs em label). Verificação: rodar import+simulação e ver E1–E4 em `/metrics`; painel "frescor do último preço".

**O9. Alertas mínimos no Grafana** 🟡 · *(tarefa NOVA)*
- Escopo: regras provisionadas (Grafana alerting, sem Alertmanager) sobre: dado velho (age E1 > 48h útil), app/DB down (O5), 5xx alto, p95 alto, falha de import (E2), simulador degradado (E4). Thresholds simples (sem SLO formal ainda).
- Arquivos: novo `infra/grafana/provisioning/alerting/*.yaml` (+ contact point).
- Risco: baixo (calibrar para evitar ruído). Verificação: parar import/DB em staging → alerta dispara.

### Sequência recomendada
**O1, O2, O5** primeiro (quick wins) → **O3** (`IResult`+LoggingBehavior, destrava O6 e a camada 3) → **O4** → **O6, O7** → **O8, O9**. A **tarefa 8** (testes de integração HTTP) é rede de segurança antes de mexer em `Program.cs`/behaviors.
