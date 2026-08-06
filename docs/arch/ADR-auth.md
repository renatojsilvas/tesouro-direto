# ADR — Autenticação e API pública multi-cliente

> Status: **APROVADO (direção)** — o dono aprovou o desenho e os pontos de decisão em 2026-08-06; a execução vira tarefas próprias.
> Tarefa: **56** do [`docs/PLANO.md`](../PLANO.md). Produz este documento; **nenhuma linha de código** foi escrita.
> Data: 2026-08-06. Escopo: **não** cobre versionamento `/v1` (tarefa 57) — ver nota final.

> **Decisões do dono (2026-08-06):**
> - **R1** — **Web BFF com service key** (o site assere ao servidor o usuário Google logado; a API pública segue só com `X-Api-Key`).
> - **R3** — rate limit **60 req/min por cliente**, **service key isenta**. Fica **abaixo** do teto por-IP do nginx (zona `api` = 30 r/s ≈ 1800/min por IP, `infra/nginx/tesouro-direto.conf:1`), então é o controle **fino por cliente**; o nginx segue como flood-guard grosso por IP.
> - **R9** — **limiter por IP** para o caminho de falha, **contando só falhas de autenticação** (key inválida) — **não** duplica o limite por-IP que o nginx já aplica; aperta especificamente o martelamento de key inválida.
> - **R10** — cookie **8h + sliding expiration**.
> - **R2/R8** aceitos como recomendado (SHA-256, key de 32 bytes prefixo `td_`; exigir `email_verified`).
> - **Pendentes de confirmação** ao virar tarefas: R5 (label `cliente` na métrica), R6 (colunas de auditoria extras), R11 (CSRF/antiforgery), R12 (concorrência do `sync`).

---

## 1. Contexto

O Tesouro Direto vira **API pública multi-cliente**. 1º cliente: um sistema de carteira externo; terceiros depois. Hoje a autenticação é uma **única API key de configuração** comparada em memória — não há conceito de usuário, conta ou key persistida. Este ADR desenha o modelo de identidade (login humano + keys por cliente) sem reabrir as decisões de produto já fixadas.

### Decisões de produto fixadas (entram no ADR, **não reabrir**)

- Login **Google (OAuth)** no site Blazor; **sem senha própria nem e-mail/SMTP** no v1.
- **Self-service de API keys por cliente** (gerar/listar/revogar numa tela); key **hasheada**, mostrada **uma vez**.
- A key identifica o **cliente-sistema**, não o usuário final.
- Chamadas programáticas seguem por **`X-Api-Key`**; Google é **só do site**.
- Cadastro **semi-aberto**: loga, mas só gera key após **APROVAÇÃO por uma TELA DE ADMIN** (nada no banco na mão).
- **Admin inicial por SEED** no boot (`ADMIN_EMAIL` do `.env`, padrão CQRS idempotente do projeto).
- **Consulta anônima do site permanece** (login só destrava a geração de key).
- A **service key atual do Web sobrevive** como credencial de serviço.
- **Rate limit por cliente EM MEMÓRIA** (uma instância; gancho para Redis registrado, **não** implementado).

---

## 2. Levantamento do código real (só leitura)

Fatos verificados no código, com `arquivo:linha`. São a base do desenho — o ADR encaixa nas costuras que já existem.

### 2.1 `ApiKeyMiddleware`

- Header **`X-Api-Key`** (`src/TesouroDireto.API/Middleware/ApiKeyMiddleware.cs:10`).
- Fonte da key: config **`ApiKey:Key`** (`ApiKeyMiddleware.cs:21`). Paths isentos vêm de `ApiKey:ExcludedPaths` (`:22`) → `["/health","/metrics","/swagger"]` (`appsettings.json:17`), casados por `StartsWithSegments` (`:62-66`).
- Validação: **SHA-256** do valor recebido e do configurado + **`CryptographicOperations.FixedTimeEquals`** (tempo constante) (`:88-99`). Key configurada vazia ⇒ inválida.
- Falha: **401** `problem+json` via `IProblemDetailsService.WriteAsync` (`:50-60`), com log `LogWarning`.
- Registro no pipeline (`src/TesouroDireto.API/Program.cs`): `UseExceptionHandler` (23) → `UseSerilogDefaults` (25) → `UseSwagger/UI` (27-28) → `UseWhen(...).UseHttpMetrics` (30-34) → **`UseMiddleware<ApiKeyMiddleware>()` (36)** → endpoints (38-46). O middleware **não** está no DI (é instanciado por `UseMiddleware`).
- **Guarda de boot**: `ApiKeyGuard.Validate` (`src/TesouroDireto.API/Extensions/ApiKeyGuard.cs`), chamado em `Program.cs:19`. Isenta `Development`/`Testing`; em prod **aborta** (`InvalidOperationException`) se a key for vazia ou uma das `BlockedKeys` (`"CHANGE-ME-IN-PRODUCTION"`, `"dev-local-key"`). Ver memória `apikey_guard_boot_prod`.
- **Web injeta `X-Api-Key`** por `DefaultRequestHeaders` no `HttpClient` tipado (`src/TesouroDireto.Web/Program.cs:16-20`), valor de config **`ApiSettings:ApiKey`** (`Web/appsettings.json:9-12`). **Não** é `DelegatingHandler` — o único handler da cadeia é o `CorrelationIdHandler`. Classe cliente: `TesouroApiClient` (`src/TesouroDireto.Web/Services/TesouroApiClient.cs:15`).

### 2.2 Correlação / log / métrica (onde plugar a identidade do cliente)

- `X-Correlation-Id`: `CorrelationIdHandler` (Web) injeta/valida (`src/TesouroDireto.Web/Services/CorrelationIdHandler.cs`); `CorrelationIdMiddleware` (API) lê/gera, grava em `HttpContext.Items["CorrelationId"]` e faz `LogContext.PushProperty` (`src/TesouroDireto.API/Middleware/CorrelationIdMiddleware.cs:13,21-24`).
- `Enrich.FromLogContext` ativo na API via `appsettings.json:13` (carregado por `ReadFrom.Configuration`). `UseSerilogDefaults` registra `CorrelationIdMiddleware` **e depois** `UseSerilogRequestLogging` (`src/TesouroDireto.API/Extensions/SerilogExtensions.cs:30-31`). Ambos rodam **antes** do `ApiKeyMiddleware`.
- `ProblemDetails` injeta `correlationId` a partir de `HttpContext.Items["CorrelationId"]` (`src/TesouroDireto.API/DependencyInjection.cs:46-58`).
- Métricas de negócio `IBusinessMetrics` (`src/TesouroDireto.Infrastructure/Observability/BusinessMetrics.cs`) e `mediatr_request_*` (`MetricsBehavior.cs`) usam **só labels de cardinalidade fechada** (enums, outcomes, `Error.Code`). Nenhum label deriva de valor livre. Ver memórias `metricas_prometheus_nomes`, `metricas_teste_delta_nao_absoluto`.

### 2.3 Modelo de dados atual

- `AppDbContext` expõe **4 `DbSet`**: `Titulos`, `PrecosTaxas`, `Tributos`, `Feriados` (`src/TesouroDireto.Infrastructure/Persistence/AppDbContext.cs:12-15`). **Nenhum conceito de usuário/conta/key persistida** (confirmado por varredura).
- Migrations em `Infrastructure/Persistence/Migrations/`, nomeação EF padrão; configs em `Persistence/Configurations/` (`IEntityTypeConfiguration<T>`), aplicadas por assembly-scan (`AppDbContext.cs:19`).
- Repos retornam **`Result`/`Result<T>`**; **read = Dapper+DTO** (exceto `TributoReadRepository`, que lê via EF), **write = EF** sem `SaveChanges` (persistência pelo handler via `IUnitOfWork`, que **é o próprio `AppDbContext`** — `Infrastructure/DependencyInjection.cs:45`). Read repos têm **decorators de cache** no DI (`:53-87`). Ver memória `repos_result_pattern`.

### 2.4 Seed idempotente no boot

- Orquestrado por `DatabaseInitializer` (`src/TesouroDireto.API/Extensions/DatabaseInitializer.cs`), disparado em `Program.cs:21` (`await app.InitializeDatabaseAsync()`), **após** `Build()` e **antes** do pipeline. Early-return em `Testing` (`:31-34`).
- Sequência: `migrator.MigrateAsync` (`:39-40`) → `sender.Send(new SeedTributosCommand())` (`:44`, falha **aborta** boot) → `ImportFeriadosCommand` só se a tabela estiver vazia (`:59-61`, falha **não** aborta).
- **Idempotência real**: `SeedTributosCommandHandler` retorna sucesso sem inserir se já há registros (`GetAllAsync` count > 0); `ImportFeriadosCommandHandler` ignora datas já existentes. `MigrateAsync` roda em Dev e Prod (só `Testing` pula). Ver memórias `migrations_always_run`, tarefa 9/21.

### 2.5 Blazor (estado de auth)

- Site **100% anônimo** hoje: zero `AddAuthentication/Authorization`, `CascadingAuthenticationState`, `AuthorizeRouteView` ou `[Authorize]` (varredura completa). `Routes.razor` usa `RouteView` puro.
- **Interactivity Server** (`Program.cs:9-10,37-38`), páginas com `@rendermode InteractiveServer` por-componente. Páginas em `Components/Pages/`, nav em `Components/Layout/NavMenu.razor`.
- **O Web não tem banco**: `TesouroDireto.Web.csproj` referencia **só** `TesouroDireto.Application`; **zero** EF/Identity/Auth. Toda persistência é da API, acessada por HTTP.

---

## 3. Decisão de arquitetura

### 3.1 Onde vivem os dados: **API dona; Web é BFF**

Não é uma impossibilidade técnica — é a decisão que preserva **dois invariantes já fixados**: (a) o Web **sem banco** (decisão de produto/arquitetura, §2.5); (b) a `ApiKeyMiddleware` validando a key **in-process, sem round-trip** por request (exigência de latência do hot path — hoje é `FixedTimeEquals` em memória, §2.1). Desses dois, a tabela `api_keys` **tem** de morar no banco que a API lê. Logo:

- **`usuarios` e `api_keys` moram no Postgres da API** (banco único; segue o padrão de tabelas + EF + migrations existente).
- **O Web autentica o humano via Google** (cookie httpOnly) e age como **BFF (backend-for-frontend)**: as telas de key/admin chamam **endpoints de gestão da API** por HTTP, com a **service key** do Web + a identidade do usuário logado.
- O Web **continua sem banco** — invariante preservada.

> **Ponto que exige aval (R1):** o Web assere ao servidor a identidade do usuário Google (delegação confiável, por ser cliente confidencial que detém a service key). Alternativas e trade-off em §6.

### 3.2 Modelo de dados

Duas tabelas novas, no padrão do projeto (`Entity<Guid>`, snake_case, `IEntityTypeConfiguration`, migration EF).

**`usuarios`**

| coluna | tipo | notas |
|---|---|---|
| `id` | uuid PK | `Entity<Guid>` |
| `google_sub` | text, **unique** | subject estável do Google (identidade canônica; e-mail pode mudar) |
| `email` | text, **unique** | do perfil Google |
| `nome` | text | do perfil Google |
| `papel` | text/enum | `User` \| `Admin` (VO/enum como string, padrão do projeto) |
| `aprovado` | bool | default `false`; admin destrava |
| `criado_em` | timestamptz | |
| `aprovado_em` | timestamptz null | auditoria |
| `aprovado_por` | uuid null | FK `usuarios.id` do admin que aprovou (auditoria) |

**`api_keys`**

| coluna | tipo | notas |
|---|---|---|
| `id` | uuid PK | identidade de baixa cardinalidade p/ log/métrica (§3.5) |
| `nome` | text | rótulo do **cliente-sistema** dado pelo dono (ex.: "carteira-prod") |
| `hash` | text, **unique** | **SHA-256** da key (nunca o texto puro) |
| `prefixo` | text | primeiros ~8 chars **não-secretos** p/ exibir na lista ("td_ab12…") |
| `dono_usuario_id` | uuid FK `usuarios.id` | quem criou (o humano); a key **representa** o cliente-sistema |
| `ativa` | bool | revogar = `false` |
| `criada_em` | timestamptz | |
| `revogada_em` | timestamptz null | auditoria |
| `ultimo_uso_em` | timestamptz null | **opcional**; se incluído, gravar com throttle p/ não escrever a cada request |

> As colunas do enunciado (`usuarios`: google_sub, email, nome, papel, aprovado; `api_keys`: hash, dono, ativa, criada_em) estão todas contempladas. Os extras (`prefixo`, `revogada_em`, `aprovado_por`, `ultimo_uso_em`) são auditoria/UX — marcados como decisão menor em §6 (R6).

### 3.3 Fluxo Google OAuth no Blazor (.NET 8)

1. Pacotes novos no Web: `Microsoft.AspNetCore.Authentication.Google` + cookie.
2. `AddAuthentication(CookieDefaults).AddCookie(...).AddGoogle(...)` com `ClientId`/`ClientSecret` de `Google:*` (via user-secrets/env — **nunca** commitado); `UseAuthentication()/UseAuthorization()` no pipeline; **cookie httpOnly + Secure + SameSite=Lax**.
3. `Routes.razor`: `RouteView` → **`AuthorizeRouteView`** + `<CascadingAuthenticationState>`; `_Imports` passa a importar `Components.Authorization`.
4. **Consulta anônima permanece**: páginas de consulta ficam `[AllowAnonymous]` (default); **só** as telas de key/admin exigem login. Login **não** muda a navegação de consulta — só destrava a geração de key.
5. **`email_verified`**: no login, o Web **exige** a claim `email_verified=true` do Google antes de qualquer `sync`/casamento por e-mail. Sem isso, um e-mail Google não-verificado poderia sequestrar o casamento do admin seed (ver R8). Cai fora com erro se falso.
6. No 1º login (com e-mail verificado), o Web chama a API `POST /admin/usuarios/sync` (service key + claims) para **upsert idempotente** do `usuario` (novo ⇒ `aprovado=false`). O `sync` trata **concorrência** (dois logins simultâneos, ou corrida com o admin seed): upsert por `google_sub`/`email` com `ON CONFLICT`/captura de `DbUpdateException` + releitura — nunca 500 por violação de unique (ver R12). Enquanto não aprovado, a tela de keys mostra "aguardando aprovação".
7. **Cookie e sessão**: `HttpOnly + Secure + SameSite=Lax`, `ExpireTimeSpan=8h` + `SlidingExpiration=true` (R10, decidido) — **não** sessão eterna. **Logout** explícito: link/botão que faz `SignOutAsync` (limpa o cookie) na nav do usuário logado.
8. **Antiforgery**: as telas de key/admin são **InteractiveServer** (SignalR), não POST de formulário tradicional — o vetor CSRF clássico é mitigado pelo canal do circuito; o `UseAntiforgery()` que já existe (`Web/Program.cs:35`) permanece. Registrar a decisão explicitamente (R11).

### 3.4 Convivência das DUAS credenciais

| | **Service key do Web** | **Key por cliente** |
|---|---|---|
| Origem | config `ApiKey:Key` (como hoje) | tabela `api_keys` (hash) |
| Identifica | o **Web/serviço** (BFF) | um **cliente-sistema** aprovado |
| Validação | `FixedTimeEquals` em memória (fast path, como hoje) | SHA-256 do header → lookup por `hash` + `ativa=true` |
| Privilégio | pode chamar **endpoints de gestão** (`/admin/*`, `/me/keys`) em nome de um usuário | só o **contrato público** de dados |
| Rate limit | isenta (ou teto alto) | por cliente (§3.6) |

A `ApiKeyMiddleware` passa a resolver **uma das duas** e a anexar a **identidade** resultante ao contexto. A guarda de boot (`ApiKeyGuard`) permanece — a service key continua obrigatória e não-default em prod.

### 3.5 `ApiKeyMiddleware` v2 — validação contra a tabela + identidade no log/métrica

Fluxo novo (preserva a ordem e o 401 problem+json atuais):

1. Path isento (§2.1) ⇒ passa direto.
2. Header ausente/vazio ⇒ **401**.
3. **Fast path**: `FixedTimeEquals` contra a service key. Match ⇒ identidade = `service` (cliente = `"web"`/serviço).
4. Senão, **SHA-256** do header → lookup por `hash` + `ativa=true` (via um `IApiKeyReadRepository` no padrão Result/Dapper, **com decorator de cache** como os demais read repos — a validação está no hot path). Match ⇒ identidade = `{ apiKeyId, clienteNome, donoUsuarioId }`.
5. Sem match ⇒ **401 problem+json** (idêntico ao atual).
6. Com identidade resolvida:
   - **Log**: `LogContext.PushProperty("ClienteId", <apiKeyId|"service">)` cobrindo `next()` (fica nos logs de domínio) **e** `IDiagnosticContext.Set("ClienteId", …)` para cair no **request-summary** do `UseSerilogRequestLogging` (que roda **fora** do middleware — sem o `Set`, o `PushProperty` não alcança aquele log; costura verificada em §2.2 e memória `fluxo_correlacao_web_api`).
   - **Métrica**: incrementar um counter dedicado **`api_key_requests_total{cliente,outcome}`** — **não** acrescentar label a `mediatr_requests_total` (evita multiplicar a cardinalidade existente). `cliente` = identificador **estável e bounded** (o conjunto de keys é aprovado à mão). Ver R5.
7. Só então segue para o **rate limiter por cliente** (§3.6), que precisa da identidade já resolvida.

> **Nota de DoS no hot path (R9):** o rate limiter por cliente roda **depois** da resolução de identidade, logo requests com key **inválida/desconhecida** não são limitados por ele — martelar keys aleatórias força SHA-256 + lookup a cada request. Com keys de alta entropia não há brute-force viável, mas há vetor de DoS. Mitigação recomendada: um limiter **por IP** (particionado por `RemoteIpAddress`, atrás do nginx via `X-Forwarded-For`) **antes** da resolução de key, cobrindo o caminho de falha. Decisão em §6.

**Cache do lookup:** o `IApiKeyReadRepository` ganha decorator de cache (padrão dos 4 read repos, §2.3). A **invalidação** desse cache **não** é genérica — o `CacheInvalidationBehavior` é um `switch` por **tipo concreto de comando** (`ImportCsvCommand`, `SeedTributosCommand`, etc.); então gerar/revogar key exige **adicionar `case`s novos** (`GenerateApiKeyCommand`/`RevokeApiKeyCommand` → `InvalidateApiKeys()`) nesse arquivo compartilhado. Esse `case` é escrito junto com os comandos, em **F4b** (não em F3) — F3 entrega o cache e o método `InvalidateApiKeys`, F4b liga a invalidação. Acoplamento explicitado no grafo (§4).

> **Ponto que exige aval (R2):** SHA-256 para a key (rápido, consistente com o middleware atual). Keys são **aleatórias de alta entropia** — SHA-256 é adequado (diferente de senha, que exigiria KDF lento). Trade-off em §6.

### 3.6 Rate limit por cliente (em memória)

- .NET 8 `AddRateLimiter` com **política particionada pela identidade** resolvida em §3.5 (`apiKeyId`; service key isenta ou teto alto).
- Rejeição: **429** `problem+json` + header **`Retry-After`** (via `OnRejected`, reusando o `IProblemDetailsService`).
- **Gancho Redis**: abstração `IRateLimitStore` (ou `PartitionedRateLimiter` atrás de uma interface) **registrada** com impl **em memória**; a variante Redis fica **declarada e não implementada** (uma instância só, como fixado). Ver memória `infra_route_nginx_blocked_by_default` não se aplica; isto é in-process.
- Posição no pipeline: **depois** da `ApiKeyMiddleware` (precisa da identidade), antes dos endpoints.

> **[DECIDIDO]** 60 req/min por cliente; service key isenta (R3). Complementado por um limiter **por IP só nas falhas de auth** (R9), que não duplica o limite por-IP grosso do nginx.

### 3.7 Endpoints de gestão (superfície BFF) + telas

Chamados **só** pelo Web (service key + usuário asserido). Autorização: usuário **aprovado** gere as **próprias** keys; **admin** aprova usuários.

- `POST /admin/usuarios/sync` — upsert idempotente no login (novo ⇒ `aprovado=false`).
- `GET /admin/usuarios?pendentes=true` — lista para a tela de admin. **[Admin]**
- `POST /admin/usuarios/{sub}/aprovar` — destrava. **[Admin]** (grava `aprovado_em`/`aprovado_por`).
- `GET /me/keys` — lista as keys do usuário (sem texto puro; mostra `prefixo`).
- `POST /me/keys` — **gera**: cria random de alta entropia, guarda `hash`+`prefixo`, **retorna o texto puro UMA vez**.
- `POST /me/keys/{id}/revogar` — `ativa=false`, `revogada_em`.

**Telas** (`Components/Pages/`, nav em `NavMenu.razor`, seção "Conta"/"Admin" gated por auth/role):
- **"Minhas API Keys"** (`/api-keys`, self-service): gerar (banner com o texto puro **mostrado uma vez** + aviso "copie agora"), listar (`prefixo`/`nome`/`ativa`/`criada_em`), revogar. Visível só a usuário **aprovado**; não-aprovado vê "aguardando aprovação".
- **"Admin"** (`/admin`, `[Authorize(Roles=Admin)]`): lista de pendentes + botão aprovar.

### 3.8 Seed de admin (`ADMIN_EMAIL`)

- `SeedAdminCommand` (MediatR, `Result`), no padrão idempotente de `SeedTributosCommand` (§2.4): lê `ADMIN_EMAIL` da config; se ausente/vazio, **no-op logado** (não aborta — dev sem admin é válido); se o usuário já existe, garante `papel=Admin`+`aprovado=true` sem duplicar; senão cria.
- **Encaixe**: no `DatabaseInitializer.InitializeAsync`, **após** `SeedTributosCommand` e feriados, **fora** de `Testing`. Como o admin loga por Google, o registro seed carrega `email`/`papel`/`aprovado`; `google_sub` fica **nulo até o 1º login**, quando o `sync` (§3.3) casa por e-mail e preenche o `google_sub`.
- **Segurança do casamento**: o `sync` só casa o registro seed (papel Admin) se a claim `email_verified=true` (§3.3.5). Sem essa checagem, qualquer conta Google com e-mail não-verificado igual a `ADMIN_EMAIL` herdaria Admin — o papel mais privilegiado do sistema (ver R8). O casamento por e-mail preenche `google_sub` **uma vez**; logins seguintes casam por `google_sub` (imutável).

> **Ponto que exige aval (R4):** casar o seed por **e-mail** (com `email_verified`) e preencher `google_sub` no 1º login (admin nasce sem `google_sub`). Alternativa: exigir `google_sub` no `.env` (pior DX). §6.

---

## 4. Ordem de dependência das fatias

```
F1 modelo de dados ─┬─> F2 seed admin
                    ├─> F3 middleware v2 (identidade + método InvalidateApiKeys) ──> F7a rate limit / F7b limiter-por-IP
                    ├─> F4a endpoints admin (usuarios) ───────────┐
                    └─> F4b endpoints self-service (me/keys) ──────┤ (F4b liga a invalidação de cache do F3)
F5 OAuth+cookie (Web, wiring puro) ──> [sync depende de F4a] ─────┼─> F6 telas Web
                                                                  ┘
```

`F1` destrava tudo. **F4 foi dividida** em **F4a** (admin sobre usuários: `sync`, listar pendentes, aprovar — regra `[Admin]`) e **F4b** (self-service de keys: `me/keys` — regra "usuário aprovado sobre os próprios recursos"); são domínios de autorização distintos e testáveis em separado. **F4b** é quem adiciona o `case` de invalidação de cache do F3 (§3.5). **F5** entrega o *wiring* de OAuth/cookie e pode nascer em paralelo, **mas** o passo "`sync` no 1º login" **depende de F4a** — F5 só fica testável fim-a-fim (usuário não-aprovado vê "aguardando") depois de F4a. `F6` precisa de F4a+F4b+F5. `F7a`/`F7b` precisam só de F3.

---

## 5. Fatias de execução (formato `PLANO.md`)

### F1. Modelo de dados: `usuarios` + `api_keys` 🟡
- **Escopo:** entidades `Usuario`/`ApiKey` (`Entity<Guid>`), `IEntityTypeConfiguration` + migration EF (tabelas, uniques em `google_sub`/`email`/`hash`), `DbSet` no `AppDbContext`; read/write repos no padrão `Result`/Dapper+DTO (leitura por `hash` e por `dono`) + EF (escrita via `IUnitOfWork`); **sem** decorator de cache ainda.
- **Arquivos:** `Domain/Usuarios/*`, `Domain/ApiKeys/*`, `Infrastructure/Persistence/Configurations/*`, nova migration, `AppDbContext.cs`, repos em `Infrastructure/Persistence/`, interfaces em `Application/Common/Interfaces/`.
- **Risco:** baixo — aditivo; não toca as 4 tabelas atuais. Cuidar dos uniques e do FK `aprovado_por`.
- **Verificação:** migration aplica; repos round-trip contra Postgres (Testcontainers, padrão da 24); `Architecture.Tests` verdes.

### F2. Seed de admin por `ADMIN_EMAIL` 🟢
- **Escopo:** `SeedAdminCommand`+handler idempotente (no molde de `SeedTributosCommand`); encaixe no `DatabaseInitializer` após tributos/feriados, fora de `Testing`; `ADMIN_EMAIL` na config/compose.
- **Arquivos:** `Application/Usuarios/SeedAdminCommand*.cs`, `API/Extensions/DatabaseInitializer.cs`, `appsettings`/compose (`ADMIN_EMAIL=${ADMIN_EMAIL:?}` no padrão `compose_required_env_no_fallback`).
- **Risco:** baixo. `ADMIN_EMAIL` ausente = no-op logado.
- **Verificação:** rodar 2× ⇒ 1 admin (`aprovado=true`, `papel=Admin`); `google_sub` nulo até 1º login; teste de integração do boot.

### F3. `ApiKeyMiddleware` v2 — tabela + identidade no log/métrica 🟡
- **Escopo:** fast-path service key (inalterado) → fallback SHA-256 + lookup `api_keys` (`ativa=true`) com **decorator de cache** (padrão `CachedTituloReadRepository`) + método `InvalidateApiKeys()` no invalidador (o `case` que o chama vem em F4b); anexar `ClienteId` via `LogContext.PushProperty` + `IDiagnosticContext.Set`; counter `api_key_requests_total{cliente,outcome}`. 401 problem+json inalterado.
- **Arquivos:** `API/Middleware/ApiKeyMiddleware.cs`, `IApiKeyReadRepository`+cache decorator, `Infrastructure/Caching/*` (método `InvalidateApiKeys`), `Infrastructure/Observability/*` (métrica), DI.
- **Risco:** médio — é o hot path e o gate de segurança. Testar service key **continua** valendo; key revogada/desconhecida ⇒ 401; invalidação de cache ao revogar/gerar.
- **Verificação:** integração HTTP (padrão da 8): service key ok; key válida resolve identidade e loga `ClienteId`; revogada ⇒ 401; `ApiKeyGuard` de boot intacto; asserção de métrica por **delta** (memória `metricas_teste_delta_nao_absoluto`).

### F4a. Endpoints de gestão — admin sobre usuários (BFF) 🟡
- **Escopo:** `POST /admin/usuarios/sync` (upsert idempotente + tratamento de concorrência, §3.3.6), `GET /admin/usuarios?pendentes`, `POST /admin/usuarios/{sub}/aprovar` (grava `aprovado_em`/`aprovado_por`). Regra `[Admin]`. Comandos/queries CQRS + `problem+json`; identidade do usuário asserida pelo Web (service key). Contratos em `API/Contracts/` (padrão `api_contracts_request_dto`); DTOs consumidos pelo Web nascem pareados em `Web/Contracts/` + `ContractParityTests` (padrão `dto_web_nasce_pareado`).
- **Arquivos:** `Application/Usuarios/*`, endpoints na API, `API/Contracts/*`, `Web/Contracts/*`.
- **Risco:** médio — trust boundary do BFF (R1) + `email_verified` (R8) + concorrência do `sync` (R12). Aprovação só por admin.
- **Verificação:** integração HTTP: `sync` 2× não duplica e não estoura unique sob corrida; aprovação só por admin (não-admin ⇒ 403); pendentes listados.

### F4b. Endpoints de gestão — self-service de keys (BFF) 🟡
- **Escopo:** `GET /me/keys` (sem texto puro, mostra `prefixo`), `POST /me/keys` (gera random alta entropia, guarda `hash`+`prefixo`, retorna texto puro **uma vez**), `POST /me/keys/{id}/revogar`. Regra "usuário **aprovado** sobre os próprios recursos". **Liga a invalidação de cache** do F3: adiciona `case GenerateApiKeyCommand/RevokeApiKeyCommand ⇒ InvalidateApiKeys()` no `CacheInvalidationBehavior` (§3.5). Contratos pareados (`dto_web_nasce_pareado`).
- **Arquivos:** `Application/ApiKeys/*`, endpoints na API, `Infrastructure/Caching/CacheInvalidationBehavior.cs`, `API/Contracts/*`, `Web/Contracts/*`.
- **Risco:** médio — nunca reexpor o texto puro (nem em log/render); usuário não-aprovado ⇒ 403; entropia mínima da key declarada (R2).
- **Verificação:** integração HTTP: gerar ⇒ texto 1× (2ª leitura não traz texto), listar sem texto, revogar ⇒ a key revogada dá 401 no `X-Api-Key` (F3, com cache invalidado); não-aprovado ⇒ 403.

### F5. OAuth Google + cookie no Web (wiring) 🟡
- **Escopo:** pacotes Google+cookie; `AddAuthentication/AddCookie/AddGoogle`; **`UseAuthentication()` → `UseAuthorization()` posicionados após `UseStaticFiles()` e antes de `UseAntiforgery()`/`MapRazorComponents`** (ordem exata, §2.5); `CascadingAuthenticationState`+`AuthorizeRouteView`; cookie `HttpOnly+Secure+SameSite=Lax` + `ExpireTimeSpan`+`SlidingExpiration` (R10); **logout** (`SignOutAsync`); exige `email_verified` (R8); `Google:*` via user-secrets/env. **Consulta anônima intacta** (`[AllowAnonymous]` default). O passo **`sync` no 1º login depende de F4a**.
- **Arquivos:** `Web/Program.cs`, `Web/Components/Routes.razor`, `_Imports.razor`, `Web/appsettings.json` (bloco `Google` **sem segredo**), `Web.csproj`.
- **Risco:** médio — 1ª auth do site. Não quebrar navegação anônima; redirect Google atrás do nginx/TLS (tarefa 44) — conferir `RedirectUri` público. **Testabilidade em CI:** login Google real não roda em CI — prever `TestAuthenticationHandler` sob `UseEnvironment("Testing")` (padrão `webappfactory_needs_db_config`).
- **Verificação:** login seta cookie; logout limpa; páginas de consulta seguem anônimas (bUnit/E2E com o handler de teste); usuário não-aprovado vê "aguardando" (fim-a-fim só com F4a).

### F6. Telas Web: "Minhas API Keys" + "Admin" 🟡
- **Escopo:** `/api-keys` (self-service, texto puro mostrado 1×) e `/admin` (`[Authorize(Roles=Admin)]`, aprovar pendentes); nav gated em `NavMenu.razor` (seção "Conta"/"Admin"); consumo via `TesouroApiClient`/BFF; logout na nav do usuário logado.
- **Arquivos:** `Web/Components/Pages/ApiKeys.razor`, `Admin.razor`, `NavMenu.razor`, serviços do Web.
- **Risco:** médio-baixo — depende de **F4a+F4b+F5**. Não vazar texto puro em log/render após o 1º show.
- **Verificação:** bUnit das telas + E2E (padrão `e2e_behavioral_tests`): gerar/listar/revogar; admin aprova e o usuário passa a gerar key.

### F7a. Rate limit por cliente em memória 🟡
- **Escopo:** `AddRateLimiter` particionado por identidade (§3.6); **60 req/min por cliente**, service key isenta; 429 problem+json + `Retry-After`; `IRateLimitStore` **em memória** com **gancho Redis declarado, não implementado**.
- **Arquivos:** `API/Program.cs`/`DependencyInjection.cs`, `Infrastructure/RateLimiting/*`.
- **Risco:** médio-baixo — depende de F3 (identidade). Não afetar rotas isentas (health/metrics/swagger); o nginx já limita por IP em outra camada (`infra/nginx/tesouro-direto.conf`) — não confundir as duas.
- **Verificação:** integração: exceder ⇒ 429 + `Retry-After`; service key não limitada; sob o teto ⇒ 200. Base para o modo "validar rate limit" do k6 (tarefa 58).

### F7b. Limiter por IP nas falhas de autenticação 🟢
- **Escopo:** limiter **por IP** (via `X-Forwarded-For` do nginx) que conta **só falhas de auth** (key inválida/ausente), disparando 429 após um teto apertado. Complementa — não duplica — o limite por-IP grosso do nginx (R9).
- **Arquivos:** `API/Middleware/ApiKeyMiddleware.cs` (contabiliza a falha) + `API/Program.cs` (política).
- **Risco:** baixo — depende de F3. Cuidar de confiar em `X-Forwarded-For` só atrás do nginx (rede interna).
- **Verificação:** integração: N falhas seguidas do mesmo IP ⇒ 429; request com key **válida** do mesmo IP **não** é bloqueada.

---

## 6. Riscos e pontos que exigem seu aval (com trade-off)

- **R1 — Web como BFF confiável (trust boundary).** O Web assere ao servidor a identidade do usuário Google usando a service key. **Recomendo A.**
  - **A) BFF-trust (recomendado):** simples, mantém o Web sem banco e a superfície pública só `X-Api-Key`. Custo: a service key é uma credencial poderosa (fala por qualquer usuário) — comprometê-la é grave. Mitigar: rotação, escopo só nos `/admin|/me`, log de `ClienteId=service`.
  - **B) API valida o `id_token` do Google** diretamente: sem trust delegado, mas a API passa a conhecer Google (contraria "Google é só do site") e complica o contrato.
  - **C) Web com banco próprio:** rejeitada — a `api_keys` **tem** de estar onde a API valida (§3.1).
- **R2 — Hash e entropia da key.** **Recomendo SHA-256** (rápido, consistente com `ApiKeyMiddleware` atual; keys são aleatórias de alta entropia, não senhas). Trade-off: um KDF lento (bcrypt/argon2) não agrega para segredos de alta entropia e machucaria o hot path de cada request. **Entropia mínima**: sugiro **32 bytes** (256 bits) de `RandomNumberGenerator`, base64url, com prefixo `td_` — torna desprezível qualquer colisão com a service key no fast-path (§3.4). Confirmar tamanho.
- **R3 — Limites do rate limit. [DECIDIDO]** **60 req/min por cliente**, service key isenta, janela fixa. Relação com o nginx: são **camadas com chaves diferentes** — nginx limita **por IP** (zona `api` 30 r/s ≈ 1800/min, `infra/nginx/tesouro-direto.conf:1,66`), o app limita **por cliente** (`api_key`). O limite por-cliente fica **abaixo** do teto por-IP para ser o que "morde" de fato; o nginx segue como flood-guard grosso.
- **R4 — Admin nasce sem `google_sub`.** Seed casa por **e-mail** e preenche `google_sub` no 1º login (melhor DX). Alternativa: `google_sub` no `.env` (pior). Recomendo o e-mail.
- **R5 — Cardinalidade do label `cliente` na métrica.** Só é seguro porque as keys são **aprovadas à mão** (conjunto bounded). Se um dia a aprovação virar automática/massiva, **remover o label** (ou trocar por bucket). Counter dedicado, nunca no `mediatr_requests_total`. Confirmar que aceita o label.
- **R6 — Colunas extras** (`prefixo`, `revogada_em`, `aprovado_por`, `ultimo_uso_em`): auditoria/UX além do enunciado. `ultimo_uso_em` implica escrita no hot path (gravar com throttle ou cair fora). Confirmar quais entram.
- **R7 — Consentimento/escopo OAuth:** só `openid email profile` (sem escopos sensíveis); tela de consentimento Google e domínio de redirect precisam do domínio de prod (tarefa 44). Sem PII além de e-mail/nome/sub.
- **R8 — [ALTO] `email_verified` no casamento do admin.** O admin seed nasce sem `google_sub` e casa por e-mail no 1º login (§3.8). **Sem** checar `email_verified=true`, qualquer conta Google com e-mail não-verificado igual a `ADMIN_EMAIL` herdaria Admin. **Recomendo exigir `email_verified`** (já embutido no desenho §3.3.5). Confirmar.
- **R9 — [MÉDIO] DoS no hot path de auth. [DECIDIDO]** Limiter **por IP** para o caminho de falha, contando **só falhas de autenticação** (key inválida). O nginx **já** limita por IP de forma grossa (30 r/s); este limiter é **semântico** — só as falhas — e por isso pode ser bem mais apertado sem afetar tráfego legítimo. Entra como refinamento da F7 (ou fatia própria F7b).
- **R10 — [MÉDIO] Expiração de sessão + logout.** Cookie com `ExpireTimeSpan` (sugiro 8h) + `SlidingExpiration` e botão de logout (§3.3.7) — **não** sessão eterna. Confirmar o TTL.
- **R11 — [MÉDIO] CSRF.** Telas são InteractiveServer (SignalR), o que mitiga o CSRF clássico; `UseAntiforgery()` permanece (§3.3.8). Registrado como decisão; confirmar que não haverá POST de formulário tradicional nessas telas.
- **R12 — [MÉDIO] Concorrência no `sync`.** `google_sub`/`email` são unique; dois logins simultâneos ou corrida com o admin seed podem violar o unique. **Recomendo** upsert com `ON CONFLICT`/captura de `DbUpdateException`+releitura (§3.3.6) — nunca 500. Confirmar.

---

## 7. Nota sobre versionamento (tarefa 57)

Este ADR **não** decide `/v1` (é a próxima tarefa). Registro apenas: **se o versionamento `/v1` entrar antes**, as rotas de auth/gestão (`/admin/*`, `/me/keys`) **nascem já versionadas** (`/v1/admin/*`, `/v1/me/keys`) e os `_links` da key incluem a versão — para não migrar duas vezes. A sequência auth×versionamento fica para o ADR da 57.
