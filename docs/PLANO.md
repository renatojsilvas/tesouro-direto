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
| 2 | ✅ `Indexador` tolerante na persistência (não quebrar EF) — concluída 2026-07-19 | Alto (corretude) | Baixo | 🟢 |
| 3 | ✅ Healthcheck real com DB check — concluída 2026-07-19 | Alto (operação) | Baixo | 🟢 |
| 4 | ✅ Enums como string no JSON (`JsonStringEnumConverter`) — concluída 2026-07-20 | Alto (API/Web) | Baixo | 🟢 |
| 5 | ✅ API key: falhar em prod se for o default — concluída 2026-07-19 | Alto (segurança) | Baixo | 🟢 |
| 6 | ✅ Exception handler global (ProblemDetails) na API — concluída 2026-07-20 | Médio | Baixo | 🟢 |
| 7 | ✅ Helper `Result`→HTTP (fim do `Contains("NotFound")`) — concluída 2026-07-20 | Médio | Baixo | 🟢 |
| 8 | Testes de integração HTTP das rotas | Alto (rede de segurança) | Baixo | 🟡 |
| 9 | Seed versionado de tributos e feriados | Muito alto (corretude) | Médio | 🟡 |
| 10 | Job Quartz de feriados | Alto (corretude) | Baixo | 🟢 |
| 11 | BCB Focus: cache + fallback | Alto (disponibilidade) | Médio | 🟡 |
| 12 | Índices para filtros comuns | Médio (performance) | Baixo | 🟢 |
| 13 | Retry/circuit breaker (Polly) nas integrações | Médio (resiliência) | Médio | 🟡 |
| 14 | Métricas de negócio/job no Prometheus | Médio (observabilidade) | Baixo | 🟡 |
| 15 | ✅ Separar contrato HTTP do `CreateTributoCommand` — concluída 2026-07-21 | Médio (arquitetura) | Baixo | 🟢 |
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

### 2. `Indexador` derivado por pattern matching 🟢 ✅ Concluída (2026-07-19)
> **Feito:** em vez de tornar `Indexador.FromName` tolerante (o que quebraria a validação de filtro em `GetTitulosQueryHandler` — `?indexador=CDI` viraria 200 vazio em vez de 400 e alteraria o contrato público), optou-se pela **2ª via do plano**: `FromName` permanece **estrito** e um novo factory `Indexador.FromPersistence(string)` (sem-falha, lossless — casa case-insensitive com os 4 conhecidos ou preserva o nome bruto) é usado **só** na conversão de leitura do EF (`TituloConfiguration`). Menor raio de dano, contrato de API intacto. Revisor confirmou por reversão de código que `Result.Value` lançava na versão antiga (teste de integração não-vacuoso via Testcontainers real) e rodou a suíte completa (312 testes verdes). Risco residual registrado: `HasMaxLength(20)` na coluna `indexador` pode truncar/estourar valor bruto exótico e longo — pré-existente, fora do escopo.
- **Escopo:** eliminar a quebra de materialização EF quando a coluna `indexador` tem valor fora da whitelist. Aplicar o mesmo padrão já usado em `TipoTitulo.DeriveIndexador` (derivação sem falhar) ou tornar a conversão de leitura tolerante (fallback em vez de `.Value` sobre `Result` de falha).
- **Arquivos:** `src/TesouroDireto.Domain/Titulos/Indexador.cs`, `src/TesouroDireto.Infrastructure/Persistence/Configurations/TituloConfiguration.cs`.
- **Risco:** mudar a semântica de `Indexador` pode afetar filtros por indexador e testes de Domain que esperam `Error` para nomes inválidos. Ajustar testes junto.
- **Verificação:** teste novo — inserir linha em `titulos` com `indexador` fora da whitelist e materializar via read repo EF **sem lançar**; suíte de Domain (`TipoTituloTests`/novos) verde. Ver memória `feedback_vo_whitelist_fragile`.

### 3. Healthcheck real com DB check 🟢 ✅ Concluída (2026-07-19)
> **Feito:** `AddHealthChecks().AddDbContextCheck<AppDbContext>()` + três endpoints — `/health` e `/health/ready` (readiness, rodam o DB check) e `/health/live` (`Predicate = _ => false`, liveness, não toca o banco). Pacote `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` v8.0.11. Docker healthcheck e os dois gates do `deploy.yml` apontam para `/health/ready`. `appsettings.json` **não** mudou: `ExcludedPaths` tem `/health` e o `ApiKeyMiddleware` usa `StartsWithSegments`, então `/health/live` e `/health/ready` já ficam isentos de ApiKey (confirmado empiricamente). Testes de middleware (`ApiKeyMiddlewareTests`, `CorrelationIdMiddlewareTests`) passaram a usar `/health/live` como path público (em `Testing` o DB é fake → readiness = 503); E2E `health.spec.ts` ajustado para corpo `"Healthy"`. Revisor executou a verificação **real** com Postgres efêmero (não vacuosa): DB no ar → `/health` e `/health/ready` = 200; DB parado → ambos = 503 e `/health/live` = 200 (3/3, refutando queda do liveness). Suíte 312/312 verde.
> Detalhado no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), item **O5** (inclui separação liveness/readiness).
- **Escopo:** trocar `MapGet("/health", () => "healthy")` por `AddHealthChecks().AddDbContextCheck<AppDbContext>()` + `MapHealthChecks("/health")`. Manter isento de ApiKey.
- **Arquivos:** `src/TesouroDireto.API/Program.cs` (e `Extensions/` se extrair).
- **Risco:** healthcheck do Docker/deploy passa a depender do banco — se o Postgres demorar a subir, o gate `/health` do deploy pode falhar (comportamento correto, mas checar o timeout do compose). Manter startup order/`depends_on`.
- **Verificação:** `/health` retorna 200 com banco OK e **503** com o Postgres parado (`docker compose stop db && curl -i /health`).

### 4. Enums como string no JSON 🟢 ✅ Concluída (2026-07-20)
> **Feito:** `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))` na API (é o mecanismo correto p/ Minimal API — não `AddControllers().AddJsonOptions`) e remoção completa do `ParseEnum` do `Tributos.razor` (envia `formBaseCalculo`/`formTipoCalculo` string direto). **O palpite do plano ("aceita ambos na desserialização? Não") estava ERRADO:** verificado empiricamente que o `JsonStringEnumConverter` do System.Text.Json aceita **string E número** na desserialização por padrão → **nenhuma quebra de contrato**, compat retroativa preservada sem código extra. Prova por 2 testes de integração HTTP com JSON cru (`tests/.../TributosEndpointsTests.cs`): `"baseCalculo":"Rendimento"`→201 e `"baseCalculo":0`→201. O revisor confirmou **não-vacuidade** revertendo o converter (o teste de string cai p/ 400 sem ele, volta a 201 com ele). GET não afetado (`TributoDto` já expõe enums como string via `.ToString()`). Suíte API **117/117** (115+2); E2E completo **22/22** verde (rebuild via `run-e2e.sh`), incluindo `tributos.spec.ts › should create a new tributo`. `EnumSerializationTests` teve só o comentário XML atualizado (premissa "não registra converter" ficou obsoleta). Ver memória `feedback_api_enums_numeric` (invertida).
- **Escopo:** registrar `JsonStringEnumConverter` (via `ConfigureHttpJsonOptions`) na API para aceitar/serializar `BaseCalculo`/`TipoCalculo` como string; remover o `ParseEnum` hardcoded do Web.
- **Arquivos:** `src/TesouroDireto.API/Program.cs`; `src/TesouroDireto.Web/Components/Pages/Tributos.razor` (remover `ParseEnum`, enviar string).
- **Risco:** clientes que hoje mandam **número** no `POST /configuracoes/tributos` quebram (mas o único cliente é o próprio Web). `JsonStringEnumConverter` aceita ambos na desserialização por padrão? Não — confirmar; se preciso, manter compat. Ajustar E2E de tributos.
- **Verificação:** `POST /configuracoes/tributos` com `"baseCalculo":"Rendimento"` retorna 201; E2E `tributos.spec.ts` verde. Ver memória `feedback_api_enums_numeric`.

### 5. API key: falhar em produção se for o default 🟢 ✅ Concluída (2026-07-19)
> **Feito:** novo `ApiKeyGuard` (`src/TesouroDireto.API/Extensions/ApiKeyGuard.cs`) com método puro e testável `Validate(string environmentName, string? configuredKey)` + overload `Validate(IConfiguration, IHostEnvironment)`, chamado em `Program.cs` **logo após `builder.Build()` e ANTES da migração** (ordem crítica: garante que a falha seja do guard, não do DB). Em `Development`/`Testing` (case-insensitive) o guard passa direto; em qualquer outro ambiente (Production/Staging/…) lança `InvalidOperationException` e aborta o boot se `ApiKey:Key` for vazia/whitespace ou o default. `appsettings.json` **não** mudou (default `CHANGE-ME-IN-PRODUCTION` preservado p/ dev local). O `deploy.yml` já injeta o secret `API_KEY` no `.env` (mitigação de risco pronta). Revisor executou a verificação **real por processo** (DLL publicado, pois `dotnet run` força Development via launchSettings): prod sem chave → aborta com a exceção do guard; prod com chave real → guard passa (falha depois só no DB); `Staging` dispara; case do ambiente robusto. **Furo achado e corrigido:** default com espaço acidental ou case diferente (`"CHANGE-ME-IN-PRODUCTION "`, `"change-me-in-production"`) passava — endurecido com `Trim()` + `OrdinalIgnoreCase` na comparação do valor. Suíte: 10/10 testes do guard, 79/79 do projeto API. **Risco residual:** o fallback do `docker-compose.yml` é `${API_KEY:-dev-local-key}` — se o secret `API_KEY` estiver vazio no deploy, a API sobe com `dev-local-key` (que o guard NÃO bloqueia, pois o escopo literal só cobre o default do appsettings). Endereçável separadamente (bloquear `dev-local-key` ou remover o fallback do compose).
- **Escopo:** no startup, se `ASPNETCORE_ENVIRONMENT != Development/Testing` e `ApiKey:Key == "CHANGE-ME-IN-PRODUCTION"` (ou vazia), lançar e abortar o boot.
- **Arquivos:** `src/TesouroDireto.API/Program.cs` ou `Middleware/ApiKeyMiddleware.cs` (validação no registro).
- **Risco:** se o `.env` de prod não tiver `ApiKey__Key`, a API deixa de subir. Garantir o secret antes de mergear.
- **Verificação:** subir com env de prod e chave default → falha explícita no log; com chave real → sobe normal.

### 6. Exception handler global (ProblemDetails) na API 🟢 ✅ Concluída (2026-07-20)
> **Feito:** `AddProblemDetails` com `CustomizeProblemDetails` injetando `correlationId` (lido de `HttpContext.Items["CorrelationId"]`) e `traceId` no corpo; `app.UseExceptionHandler()` registrado como middleware **mais externo** (antes do `CorrelationIdMiddleware`, capturando todo o downstream). O `CorrelationIdMiddleware` passou a gravar `context.Items["CorrelationId"]` (antes o valor só existia no header e no `LogContext`), habilitando sua leitura no corpo. Os **dois** caminhos de 401 do `ApiKeyMiddleware` agora escrevem `application/problem+json` via `IProblemDetailsService` (o `correlationId` vem do mesmo `CustomizeProblemDetails`). Endpoint `GET /_test/throw` mapeado **só em `Testing`** para forçar exceção nos testes. **Palpite do plano corrigido:** `traceId` NÃO é auto-injetado em Minimal API fora de MVC — foi adicionado explicitamente (`Activity.Current?.Id ?? TraceIdentifier`). 3 testes de integração novos (500→problem+json, 401→problem+json, eco de `X-Correlation-Id` conhecido no corpo). Revisor confirmou **não-vacuidade** revertendo a injeção do `correlationId` (os 3 testes caem) e rastreou o stack trace real da exceção provando a ordem do pipeline (Correlation grava `Items` antes de `next`, exceção sobe e é capturada no mesmo `HttpContext`). Suíte API **120/120** verde (117+3), sem regressão na tarefa 8. Cercas respeitadas: mapeamento Result→status (tarefa 7), behaviors MediatR (O3) e validação de key/ExcludedPaths intactos.
> Também coberto no [Anexo — Observabilidade](#anexo--observabilidade-em-3-camadas), item **O4** (captura de erros, camada 1).
- **Escopo:** adicionar `UseExceptionHandler` + `AddProblemDetails` para que exceções não tratadas virem resposta `application/problem+json` consistente (hoje viram 500 cru). Padronizar também o corpo do 401 do `ApiKeyMiddleware`.
- **Arquivos:** `src/TesouroDireto.API/Program.cs`, `src/TesouroDireto.API/Middleware/ApiKeyMiddleware.cs`.
- **Risco:** baixo; muda o formato do corpo de erro 500 (nenhum cliente depende do corpo cru hoje).
- **Verificação:** endpoint que lança exceção retorna `problem+json` com `traceId`/CorrelationId; 401 passa a ter corpo.

### 7. Helper `Result`→HTTP compartilhado 🟢 ✅ Concluída (2026-07-20)
> **Feito:** novo `src/TesouroDireto.API/Extensions/ResultExtensions.cs` com `ToHttpResult`/`ToHttpResult<T>(this Result[/<T>], Func<...,IResult> onSuccess)`. Mapa **por estrutura (sufixo)**, não substring: `Error.Code.EndsWith(".NotFound", Ordinal)` → **404**; **todo o resto** → **400**. Deliberadamente **sem** 409/502/500 — assim `Projecao.HttpError`/`Projecao.UrlNotConfigured` (usados no simulador) permanecem 400 como hoje. O `Contains("NotFound")` do PUT foi eliminado; o helper foi aplicado nos **4** `Endpoints/*.cs`, removendo todo o boilerplate `IsSuccess ? … : …` (grep confirma zero `Results.NotFound`/`Results.BadRequest`/`Contains`). Falha sai como `application/problem+json` da tarefa 6: o helper escreve via `IProblemDetailsService.WriteAsync` (herda `correlationId`+`traceId` do `CustomizeProblemDetails`), com `code`/`Detail` no corpo. **Efeito autorizado pelo autor** (decisão registrada): `POST /simulador` e `/simulador/cenarios` passam a **404** para `Titulo.NotFound`/`Projecao.NotFound` (antes 400 p/ qualquer falha) — 2 asserções da suíte atualizadas; e as rotas by-nome (`GET /titulos/preco-atual?nome=`, `/titulos/precos?nome=`) passam a **400** para nome vazio (`Titulo.InvalidNome` = validação → 400, antes 404 incondicional), agora com testes dedicados. Revisor confirmou os 4 critérios por execução real + falsificação ativa (reverter o helper derruba os 3 testes de problem+json; substituir `WriteAsync` por JSON manual derruba correlationId/traceId). Descoberta não-óbvia: no .NET 8 com `AddProblemDetails`, **`Results.Problem` TAMBÉM** dispara o `CustomizeProblemDetails` (a premissa comum de que só `WriteAsync` integra é falsa) — comentário do helper corrigido. Suíte **366/366** (API 125). Ver memória `feedback_result_http_status_map`.
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
- **Herdado da tarefa 7:** o mapa `Result`→HTTP passou a devolver **404** (era 400) quando `Projecao.NotFound` sobe do `FocusBcbService` (BCB sem dados). Esse caminho depende do BCB por HTTP externo e **não tem teste de integração** hoje. Ao montar o `FakeHttpMessageHandler` desta tarefa, adicionar um caso que force `Projecao.NotFound` e assertar **404 `application/problem+json`** em `POST /simulador` — cobrindo o buraco deixado pela tarefa 7.

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

### 15. Separar contrato HTTP do `CreateTributoCommand` 🟢 ✅ Concluída (2026-07-21)
> **Feito:** novo `src/TesouroDireto.API/Contracts/CreateTributoRequest.cs` (`sealed record`, namespace `TesouroDireto.API.Contracts`) espelhando campo a campo o `CreateTributoCommand` (`Nome`, `BaseCalculo`, `TipoCalculo`, `Faixas`, `Ordem`, `Cumulativo` — mesmos nomes/tipos/ordem, enums como string da tarefa 4). O `MapPost("/configuracoes/tributos")` passou a bindar `CreateTributoRequest request` e mapear para o comando **dentro** do handler (mesmo padrão do `MapPut`/`UpdateTributoRequest`); retorno via helper `ToHttpResult` (tarefa 7) intacto. **Decisão de escopo:** `Faixas` reusa o `FaixaDto` da Application (não se criou um `FaixaRequest` próprio) para manter consistência com o `UpdateTributoRequest` existente e não expandir a tarefa. **JSON de entrada byte-idêntico** — prova não-vacuosa: os testes de POST em `TributosEndpointsTests.cs` serializam um `CreateTributoCommand` (tipo da Application) direto no corpo e bindam com sucesso no `CreateTributoRequest`, sem nenhuma asserção alterada. Revisor executou as 4 verificações com evidência real: (a) arquivo de teste inalterado via `git diff` + API **125/125** (8/8 isolado); (b) **E2E 22/22** com rebuild genuíno do container, incluindo `tributos.spec.ts › should create a new tributo`; (c) grep confirma `CreateTributoCommand` só na construção interna do handler, nenhum handler o binda como request; (d) Architecture.Tests **13/13**. Flake infra (Testcontainers/Npgsql SSL race) verde no re-run. **Sugestão de follow-up (não feita):** `UpdateTributoRequest` (aninhado em `ConfiguracaoEndpoints.cs`) e ambos os contratos ainda referenciam `FaixaDto` da Application — vazamento residual endereçável junto num contrato de faixa próprio de `Contracts/`.
- **Escopo:** introduzir um `CreateTributoRequest` em `API/Contracts/` e mapear para o comando no endpoint, parando o vazamento da camada Application no contrato público.
- **Arquivos:** novo `src/TesouroDireto.API/Contracts/CreateTributoRequest.cs`; `Endpoints/ConfiguracaoEndpoints.cs`.
- **Risco:** baixo; o JSON de entrada não muda se o request espelhar o comando. Cobrir com teste de integração (tarefa 8).
- **Verificação:** `POST /configuracoes/tributos` mantém o mesmo contrato; mudança interna no comando não altera o request.

### 16. Cliente tipado no Web (dedup das 5 páginas) 🔴
- **Escopo:** criar um `TesouroApiClient` em `Web/Services/` encapsulando `CreateClient`, montagem de request, desserialização e `ApiError`; refatorar as 5 páginas para usá-lo.
- **Arquivos:** novo `src/TesouroDireto.Web/Services/TesouroApiClient.cs`; `Components/Pages/{Titulos,Historico,Tributos,Simulador,Cenarios}.razor`.
- **Risco:** **médio** — toca todas as telas; regressão de UI. Fazer incremental (uma página por vez) e apoiar nos E2E.
- **Verificação:** E2E web (todas as specs) verde após o refactor; nenhuma página monta HttpClient/`ApiError` local.

### 17. Observabilidade no Web (Serilog/Loki) 🟢 ✅ Concluída (2026-07-21)
> **Feito:** ver item **O2** do anexo (esta tarefa = O2). Serilog+Loki no Web replicando o padrão O1, `CorrelationIdHandler` (`DelegatingHandler`) injetando `X-Correlation-Id` em toda chamada à API, correlação Web→API validada ao vivo no Loki.
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

**O1. Logs em JSON + remover sink Loki duplicado** 🟢 ✅ Concluída (2026-07-21)
> **Feito:** a duplicação do Loki vinha de `AddSerilog` chamar `.ReadFrom.Configuration` (que lia `WriteTo[1]=GrafanaLoki` do `appsettings.json`) **e** adicionar outro `.WriteTo.GrafanaLoki` em código → cada linha ia 2× pro Loki (o Console não duplicava porque não havia `.WriteTo.Console` em código). Correção: **removido o array `WriteTo` inteiro do `appsettings.json`** (Console E GrafanaLoki — senão o Console duplicaria ao movê-lo pro código); `MinimumLevel`/`Enrich:["FromLogContext"]` preservados. `SerilogExtensions.cs` passou a configurar os dois sinks no código: `.WriteTo.Console(new CompactJsonFormatter())` (pacote `Serilog.Formatting.Compact` 3.0.0 já vem transitivo do `Serilog.AspNetCore`, sem `PackageReference` novo) + `.WriteTo.GrafanaLoki(lokiUri, labels:[job=tesouro-direto-api])`, mais 3 enrichers via Serilog core (sem novo pacote): `.Enrich.WithProperty("service","tesouro-direto-api")`, `("environment", context.HostingEnvironment.EnvironmentName)`, `("MachineName", Environment.MachineName)`. **Furo de config resolvido:** o URI do Loki era lido de `Serilog:WriteTo:1:Args:uri`; sem o `WriteTo` no appsettings, a env var `Serilog__WriteTo__1__Args__uri` do `docker-compose.yml` deixaria um `WriteTo:1` **sem `Name`** no config mesclado (risco de quebrar o `ReadFrom.Configuration`) — trocada por uma chave dedicada `Loki__Uri`, lida no código como `Loki:Uri` (fallback `http://localhost:3100`). `datasources.yml` validado (não editado): `derivedField` `CorrelationId` com `matcherRegex '"CorrelationId":"([^"]+)"'` casa o campo top-level do JSON. Revisor confirmou as 3 verificações com evidência real: (a) suíte da 8 **125/125**; (b) stack local rebuildada, 1 requisição com `X-Correlation-Id: o1-verify-ABC123` → Loki retornou **exatamente 1 entrada** (não 2), JSON parseado, com `CorrelationId`+`service`+`environment=Production`+`MachineName`; (c) grep confirma sink Loki em 1 só lugar (código), zero no appsettings. Boot não quebra sem a seção `WriteTo`; `MinimumLevel` intacto; `docker-compose.e2e.yml` não usava Loki. **Follow-ups registrados (fora do escopo fechado, pré-existentes):** (1) o sink Loki não recebe `textFormatter` → usa o formatter default (`Message`/`MessageTemplate`/`level:"info"` minúsculo), formato divergente do CompactJson do Console — o JSON com `CorrelationId` funciona, mas convém padronizar (passar um formatter ao Loki); (2) **bug no dashboard** `infra/grafana/dashboards/tesouro-direto.json`: painéis de nível filtram `level = "Information"`/`Warning`/`Error` mas o Loki emite `level:"info"` (minúsculo/abreviado) → painéis "Log Level Over Time" ficam sempre vazios (verificado ao vivo: `level="Information"`→0, `level="info"`→60). Ambos endereçáveis junto ao O2 (Serilog no Web).
- Escopo: Console com `CompactJsonFormatter`; remover a duplicidade do sink Loki (`appsettings.json:15` **e** `SerilogExtensions.cs:16` → manter só no código); enrichers `environment`/`service`/`MachineName`; validar o `derivedField` do CorrelationId no Grafana.
- Arquivos: `src/TesouroDireto.API/Extensions/SerilogExtensions.cs`, `src/TesouroDireto.API/appsettings.json`, `infra/grafana/provisioning/datasources/datasources.yml`.
- Risco: baixo (formato de log local muda; parsing do Loki melhora). Verificação: Loki mostra linhas JSON com `CorrelationId` e **sem** duplicata.

**O2. Correlação ponta-a-ponta Web→API** 🟢 ✅ Concluída (2026-07-21) · *(= tarefa 17)*
> **Feito:** novo `src/TesouroDireto.Web/Extensions/SerilogExtensions.cs` replicando o padrão O1 (Console `CompactJsonFormatter` + `GrafanaLoki`, enrichers `service`/`environment`/`MachineName`, `Loki:Uri` com fallback `http://localhost:3100`), com `service`/`job=tesouro-direto-web` (distinto de `-api`, mas o `derivedField` `CorrelationId` do Grafana ainda linka os dois). Novo `src/TesouroDireto.Web/Services/CorrelationIdHandler.cs` (`DelegatingHandler`): reusa um `X-Correlation-Id` válido preexistente (mesmo regex `^[a-zA-Z0-9\-]{1,64}$` da API) ou gera `Guid`, injeta o header em **toda** chamada à API, faz `LogContext.PushProperty("CorrelationId", id)` e loga "Chamando API"/"API respondeu {StatusCode}" (agnóstico ao status). Registrado via `.AddHttpMessageHandler<CorrelationIdHandler>()` no client `"TesouroDiretoApi"` existente (cliente tipado adiado p/ tarefa 16); `docker-compose.yml` ganhou `Loki__Uri` no serviço `web`. **Bug pego pelo revisor (real, refutado ao vivo):** o `UseSerilog` do Web foi replicado da API **sem** `.Enrich.FromLogContext()` — a API só o tinha via `appsettings.json` (`Serilog:Enrich:["FromLogContext"]`), que o Web não possui → o `PushProperty` virava no-op e **nenhum** log do Web carregava `CorrelationId` (critérios b/c falhavam, derivedField do Grafana não casava do lado Web). Corrigido adicionando `.Enrich.FromLogContext()` no código do Web. Verificação viva (stack Docker local, imagem web rebuildada): (a) build limpo, `API.Tests` **125/125**, E2E **22/22**; (b) `GET /titulos` → **mesmo** `CorrelationId` (`02c32b3b-…`) nos logs de `service=tesouro-direto-web` **e** `tesouro-direto-api`; (c) POST inválido → `application/problem+json` (tarefa 6) com `correlationId` batendo com o log da API; (d) `docker compose stop loki` + restart do web → Web sobe e responde 200 (sink não-bloqueante). Ver memória `project_fluxo_correlacao_web_api`.
- Escopo: Serilog+Loki no Web + `DelegatingHandler` que injeta `X-Correlation-Id` nas chamadas à API (hoje só manda `X-Api-Key`).
- Arquivos: `src/TesouroDireto.Web/Program.cs`, novo `src/TesouroDireto.Web/Services/CorrelationIdHandler.cs`.
- Risco: baixo (Web passa a depender do Loki no boot — sink não-bloqueante). Verificação: um único `CorrelationId` em Web+API para a mesma ação.

**O3. `LoggingBehavior` (MediatR) + `IResult` para captura de falhas sem exceção** 🟡
- Escopo: behavior espelhando `CacheInvalidationBehavior` que loga início/fim e captura `Result.IsFailure` como Warning (`Error.Code`/`Description`). Introduzir interface mínima `IResult { bool IsSuccess; Error Error; }` em `Result`/`Result<T>` para leitura genérica (evita o pattern-match por tipo que hoje cai no `default`). Logar linhas de CSV inválidas individualmente.
- Arquivos: novo `LoggingBehavior` (Application/Common ou Infrastructure), `src/TesouroDireto.Domain/Common/Result.cs` (+`IResult`, aditivo), registro em `DependencyInjection.cs`, `ImportCsvCommandHandler.cs`.
- Risco: baixo–médio — toca `Result` do Domain; rodar `Architecture.Tests`. Verificação: comando que falha → Warning no Loki, sem 500.

**O4. Exception handler global (ProblemDetails)** 🟢 ✅ Concluída (2026-07-20) · *(= tarefa 6)*
- Escopo/arquivos/verificação: ver tarefa 6. Fecha a captura de erros da camada 1 (exceções não tratadas → `problem+json` com CorrelationId; corpo do 401 padronizado).

### Camada 2 — Métricas técnicas (latência, erro, saturação, healthcheck)

**O5. Healthcheck real + liveness/readiness** 🟢 ✅ Concluída (2026-07-19) · *(= tarefa 3, expandida)*
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
