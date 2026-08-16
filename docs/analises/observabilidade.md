# Análise — Observabilidade

> **Retrato datado de 2026-07-18, superado pela tarefa 77.** Este levantamento descreve a stack
> local de então (`grafana`+`loki`+`prometheus`+`promtail`+`node-exporter`). Desde a tarefa 77 essa
> stack saiu do ar (77.4) e foi substituída por um único container `alloy`, que faz scrape/tail
> local e remote-write/loki-write para o Grafana Cloud (free tier); alerting e dashboards agora
> vivem na nuvem. Para o estado atual, ver [`infra/alloy/README.md`](../../infra/alloy/README.md).
> O corpo abaixo **não foi reescrito** — é registro histórico.

> Levantamento **somente-leitura** do estado atual (logs, métricas técnicas, métricas de negócio, alertas, healthchecks), guiado por [`docs/MAPA.md`](../MAPA.md) §4.
> Evidência com `arquivo:linha`. Nada foi alterado.

## Sumário executivo

| Pilar | Estado | Nota |
|-------|--------|------|
| **Logs** | 🟡 Parcial | Serilog→Loki na API com CorrelationId por request e request logging com latência. Web sem Serilog; sink Loki duplicado; labels mínimas; formato não-JSON explícito. |
| **Correlação** | 🟡 Parcial | Sólida **dentro** da API (middleware valida/propaga/retorna). **Quebra** na fronteira Web→API (Blazor não propaga `X-Correlation-Id`). |
| **Métricas técnicas** | 🟡 Básico | Só o que `UseHttpMetrics` + collectors default do prometheus-net dão (latência/erro/in-progress HTTP, GC/process/threads). Sem métricas de dependências externas nem de saturação de DB/fila. |
| **Métricas de negócio** | 🔴 Inexistente | Zero. Nenhum counter/gauge de domínio; todo evento de negócio vive só em log. |
| **Alertas** | 🔴 Inexistente | Nenhum Grafana alerting, Prometheus rules ou Alertmanager. Coleta e exibe, mas nada notifica. |
| **Healthchecks** | 🔴 Raso | `/health` retorna string fixa; não toca banco/dependências. Gate de deploy herda o falso-positivo. |

**Leitura geral:** a stack de observabilidade está **montada e cabeada** (Serilog→Loki, prometheus-net→Prometheus→Grafana, dashboard com 14 painéis, CorrelationId), mas opera no nível de **infraestrutura HTTP/runtime**. Falta a camada que responde *"o negócio está saudável?"* — métricas de domínio, alertas e um healthcheck que detecte dependência caída.

---

## 1. Logs

### O que existe
- **Serilog** configurado em `src/TesouroDireto.API/Extensions/SerilogExtensions.cs`, ativado em `Program.cs:11`. `ReadFrom.Configuration` (`appsettings.json`) + sink `GrafanaLoki` adicionado no código (`SerilogExtensions.cs:16-18`).
- **Sinks:** Console (formato texto default, sem template) + GrafanaLoki (URI de `Serilog:WriteTo:1:Args:uri`, fallback `http://localhost:3100`).
- **Níveis:** `MinimumLevel` Default `Information`; overrides `Microsoft.AspNetCore = Warning`, `System = Warning` (`appsettings.json:6-12`).
- **Enrichers:** só `FromLogContext` (`appsettings.json:17`).
- **Request logging:** `UseSerilogRequestLogging()` (`SerilogExtensions.cs:26`) → um log por request com método, path, status e **`Elapsed` (latência)**, já enriquecido com `CorrelationId`.
- **Loki** (`infra/loki/loki-config.yml`): retention **30d** com compactor (`retention_enabled: true`), storage `tsdb`+`filesystem`, schema v13, `auth_enabled: false`, single-node. (Ver memória `feedback_loki_serilog_labels`.)

### Logs no código de negócio (cobertura escassa)
| Componente | Eventos logados | Nível |
|-----------|-----------------|-------|
| `ImportCsvCommandHandler` (`:24`, `:100-102`) | início + resumo (títulos/preços/skipped/erros) | Info |
| `CsvImportJob` (`:13`, `:19-22`, `:26-27`) | início, resultado, falha | Info/Error |
| `CsvImportService` (`:39`, `:77`) | HTTP != 200, exceção no download | Error |
| `FocusBcbService` (`:45`, `:51-53`) | só falhas (sem sucesso/latência) | Error |
| `FeriadoImportService` (`:21`,`:27`,`:40`,`:46`) | config/URL/download/HTTP | Error |
| `ApiKeyMiddleware` (`:33`, `:40`) | request sem/ com API key inválida (+`{Path}`) | Warning |

### Gaps de logs
- **Web/Blazor sem Serilog/Loki** (`src/TesouroDireto.Web/Program.cs`) — só logging default do ASP.NET; nada vai para o Loki. Ponto cego da camada que faz as chamadas à API.
- **Sink Loki registrado em duplicidade**: `appsettings.json:15` (`WriteTo[1]`) **e** `SerilogExtensions.cs:16` — risco de escrita dupla.
- **Labels mínimas** no Loki: só `job=tesouro-direto-api`; sem `environment`/`app`/`version`/`level`.
- **Formato não-estruturado explícito**: Console sem formatter JSON; o **derivedField do Grafana espera JSON** (`"CorrelationId":"..."`, `datasources.yml:19-23`) — possível inconsistência de parsing a validar em runtime.
- **Request logging sem enrich customizado** (sem ClientIP/UserAgent/host).
- **Linhas de CSV inválidas não são logadas** individualmente (só contadas em `linhasComErro`).
- Enrichers limitados; sem seção Serilog por ambiente (`appsettings.Development.json`); coexistência de `Logging.LogLevel` e `Serilog.MinimumLevel` (a de Serilog é a efetiva) pode confundir.

## 2. Correlação por request

### O que existe
- `CorrelationIdMiddleware` (`src/TesouroDireto.API/Middleware/CorrelationIdMiddleware.cs`), registrado em `SerilogExtensions.cs:25` (via `UseSerilogDefaults`, `Program.cs:25`) **antes** de métricas e ApiKey.
- Header `X-Correlation-Id`; reusa se casar regex `^[a-zA-Z0-9\-]{1,64}$`, senão gera GUID (`:26-38`) — valida e limita tamanho (previne log injection).
- `LogContext.PushProperty("CorrelationId", ...)` (`:20`) → todos os logs do request carregam o ID; devolvido no response via `OnStarting` (`:14-18`).

### Gaps de correlação
- **Sem trace ponta-a-ponta Web→API**: o `HttpClient` `TesouroDiretoApi` (`Web/Program.cs:8-12`) só injeta `X-Api-Key`, **não propaga `X-Correlation-Id`**. Todas as páginas usam esse client (Titulos/Historico/Cenarios/Simulador/Tributos) sem setar correlação. Resultado: cada chamada do Blazor gera um ID novo na API — a jornada do usuário não é rastreável de ponta a ponta. Falta um `DelegatingHandler`.

## 3. Métricas técnicas

### O que existe
- `prometheus-net.AspNetCore` v8.2.1 (`API.csproj:14`). `UseHttpMetrics()` (`Program.cs:26`, antes do ApiKey → captura tudo) + `MapMetrics()` `/metrics` (`Program.cs:35`).
- **HTTP:** `http_request_duration_seconds` (histogram, latência por método/status/endpoint), `http_requests_in_progress` (gauge), `http_requests_received_total` (counter).
- **Runtime .NET** (collectors default): `process_*`, `dotnet_collection_count_total`, memória, threads.
- **`prometheus.yml`:** scrape 15s, único job `tesouro-direto-api` → `app:8080/metrics`.

### Gaps técnicos
- **Dependências externas sem instrumentação**: os 3 typed clients (`DependencyInjection.cs:75/80/85` — CSV, Feriados, BCB) **não** usam `UseHttpClientMetrics()`; sem latência/erro por dependência. BCB/ANBIMA/Tesouro fora do ar não aparece em métrica.
- **Saturação incompleta**: só process/GC/threads default; sem métrica de **conexões/pool do EF/Npgsql** nem de **fila do Quartz**.
- **Erros só genéricos**: apenas o `code` do histograma HTTP; sem contagem de falhas por comando/handler MediatR.
- **Prometheus não faz self-scrape** nem de exporters (node_exporter, postgres_exporter, cAdvisor).

## 4. Métricas de negócio

### Estado: 🔴 inexistente (confirmado)
Busca por `Counter|Gauge|Histogram|Meter|System.Diagnostics.Metrics` em todo `src/` → só as 3 linhas do `Program.cs` + falsos-positivos de `DynamicParameters` (Dapper). **Nenhuma instrumentação customizada.**

Todo evento de domínio vive **apenas em log**, nunca como métrica:
- Importação: `CsvImportJob.cs:18-27` e `ImportCsvCommandHandler.cs:98-102` têm os números (títulos criados, preços inseridos/ignorados, linhas com erro) mas só em `LogInformation`.
- Simulação: `SimularCommandHandler.cs` — sem métrica de simulações executadas.
- Tributos: `CreateTributoCommandHandler`/`UpdateTributoCommandHandler` — sem métrica.

### Gaps de negócio (todos ausentes)
- Importações executadas (sucesso/falha) — counter.
- Preços/linhas processados, títulos criados, preços ignorados, linhas com erro — counters.
- Simulações executadas (por indexador?) — counter.
- Tributos criados/atualizados — counter.
- **Frescor do dado**: idade do último preço importado (gauge) — a métrica mais valiosa para alertar "dados desatualizados", e não existe.

> Nota sobre o exemplo do pedido ("agendamentos criados/cancelados, conversão"): não se aplica a este domínio — não há agendamentos/funil de conversão. Os equivalentes de negócio aqui são importação, simulação e configuração de tributos, listados acima.

## 5. Alertas

### Estado: 🔴 inexistente (confirmado)
- Nenhuma regra em lugar algum: sem Grafana alerting (`infra/grafana/provisioning/alerting/` não existe), sem Prometheus `rule_files`/`alerting:` (`prometheus.yml` tem só `global` + 1 scrape), sem Alertmanager no compose, sem `*.rules.yml`.

### Gaps de alertas
- Sem notificação para: 5xx alto, latência p95 elevada, app down, **DB down**, import falho, dado desatualizado.
- **Sem SLOs/SLIs formais**: o dashboard mostra p50/p95/p99 e error rate, mas sem thresholds, error budget ou burn-rate.

## 6. Healthchecks

### O que existe
- **App:** `/health` → `Results.Ok("healthy")` (`Program.cs:29`), string fixa. **Sem** `AddHealthChecks`/`AddDbContextCheck`/`MapHealthChecks` (confirmado por grep). Migração roda no boot (`Program.cs:18-23`), então DB indisponível derruba o start — mas não há check contínuo.
- **Docker (prod, `docker-compose.yml`):** `app` `curl -sf .../health` (interval 10s, retries 10, start 30s) depende de `db` healthy; `db` `pg_isready` (interval 5s). `web`, `grafana`, `loki`, `prometheus` **sem healthcheck**.
- **Docker (e2e):** `app`/`web` usam teste de porta TCP `/dev/tcp` (não batem em `/health`). (Ver memória `feedback_docker_aspnet_no_curl`.)
- **Deploy gate** (`.github/workflows/deploy.yml`): e2e espera `/health` + `/` (`:47-55`); deploy espera `/health` (`:125-127`). Gate = `/health` raso.

### Gaps de healthchecks
- `/health` **não valida DB nem dependências** → falso positivo (processo vivo, banco caído = 200). O gate de deploy herda isso; deploy pode ser "sucesso" com banco degradado.
- **Sem separação liveness vs readiness** (`/health/live` vs `/health/ready`).
- **Serviços sem healthcheck** no compose de prod (`web`, `grafana`, `loki`, `prometheus`).
- **Inconsistência prod × e2e** (curl `/health` vs porta TCP).
- **Sem smoke test funcional pós-deploy** (bater num endpoint de negócio) nem rollback automático.

---

## 7. Dashboards (contexto)

Único dashboard `infra/grafana/dashboards/tesouro-direto.json` (uid `tesouro-direto-api`), 14 painéis, refresh 10s. Datasources provisionados com **UID fixo** `prometheus`/`loki` (`datasources.yml`, batem com o JSON — ver memória `feedback_grafana_provisioning_fixed_uid`).
- **Prometheus:** Request Rate, Response Time p50/p95/p99, Error Rate 4xx/5xx, Requests by Status, Top Endpoints by Latency, CPU, Memory, Active Connections, Uptime, GC.
- **Loki:** stream de logs, Logs by Level, Trace by CorrelationId (template `$correlationId`).
- **Cobertura:** 100% infra HTTP/runtime; **zero painel de negócio** (import, frescor de dados, simulações) — consequência direta do gap §4.

---

## Gaps consolidados por módulo

| Módulo | Logs | Correlação | Métrica técnica | Métrica negócio | Health |
|--------|------|-----------|-----------------|-----------------|--------|
| **API (entrada HTTP)** | ✅ req logging + níveis | ✅ middleware | ✅ HTTP básico | 🔴 nenhuma | 🔴 `/health` raso |
| **Web (Blazor)** | 🔴 sem Serilog/Loki | 🔴 não propaga ID | 🔴 sem métricas | 🔴 n/a | 🔴 sem healthcheck no compose |
| **Import CSV (job + handler)** | 🟡 resumo, sem erro por linha | ✅ (dentro da API) | 🔴 sem métrica de job/duração | 🔴 sem counters nem frescor | — |
| **Import Feriados** | 🟡 só erros | ✅ | 🔴 | 🔴 | — |
| **BCB Focus (integração)** | 🟡 só falha | ✅ | 🔴 sem latência/erro por dependência | 🔴 | — |
| **Persistência (EF/Dapper)** | 🔴 sem logs | ✅ | 🔴 sem pool/conn | 🔴 | 🔴 não checado por `/health` |
| **Quartz (scheduler)** | 🟡 via job | ✅ | 🔴 sem métrica de fila/execução | 🔴 | — |
| **Infra (Loki/Prom/Grafana)** | ✅ retention 30d | ✅ derivedField | ✅ scrape | — | 🔴 sem alertas, sem healthcheck |

## Prioridades sugeridas (maior retorno primeiro)

1. **Healthcheck real com DB check** (`AddDbContextCheck`) — elimina falso-positivo do gate de deploy. *(= tarefa 3 do [`PLANO.md`](../PLANO.md))*
2. **Métricas de negócio + frescor do último preço** e duração/sucesso do job de import — habilita o primeiro alerta útil ("dados desatualizados"). *(= tarefa 14)*
3. **Alertas mínimos** no Grafana: app down, DB down, 5xx alto, p95 alto, dado velho.
4. **Propagar CorrelationId Web→API** (`DelegatingHandler`) + **Serilog/Loki no Web** — trace ponta a ponta. *(= tarefa 17)*
5. **`UseHttpClientMetrics`** nos typed clients — latência/erro das dependências externas.
6. **Corrigir sink Loki duplicado** e validar o parsing JSON do derivedField do CorrelationId.
