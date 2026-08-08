# ADR — Versionamento da API (`/v1`)

> Status: **PROPOSTO** — aguardando aprovação do dono antes de virar tarefas de execução.
> Tarefa: **57** do [`docs/PLANO.md`](../PLANO.md). Produz este documento; **nenhuma linha de código** foi escrita.
> Data: 2026-08-08. Escopo: decide **como** e **quando** versionar a API pública; não reabre o desenho de autenticação (tarefa 56 / [`ADR-auth.md`](./ADR-auth.md)).
>
> **Nota sobre o estado real do repo nesta data:** as fatias de execução da 56 (**59–67**) já estão **concluídas e mergeadas** (`docs/PLANO.md:739-813`; `git log` mostra o merge da 67 como topo do `main`). Ou seja, na prática **a auth já nasceu sem `/v1`** — a pergunta "versionar antes ou depois da auth" (§4.5) já tem um lado consumado. Este ADR ainda apresenta os dois lados (era a pergunta em aberto quando a 56/57 foram planejadas juntas, `docs/PLANO.md:702`) e explica por que o resultado observado é também a recomendação.

---

## 1. Contexto

O Tesouro Direto está virando **API pública multi-cliente** (1º cliente: um sistema de carteira externo; terceiros depois — `ADR-auth.md:19`). Hoje **não existe nenhum prefixo de versão**: todas as rotas de negócio (`/titulos`, `/simulador`, `/configuracoes/tributos`, `/importacao`, `/admin/usuarios`, `/me/keys`) vivem na raiz. Antes de abrir a API a terceiros, é preciso decidir **onde** a versão vive (path vs. header) e **como migrar** sem quebrar o único consumidor de hoje (o próprio Web).

---

## 2. Levantamento do código real (só leitura)

Fatos verificados no código, com `arquivo:linha`.

### 2.1 Registro de rotas hoje

- `Program.cs:45-50` registra os grupos de endpoint em sequência, sem nenhum prefixo comum: `app.MapImportacaoEndpoints(); app.MapTituloEndpoints(); app.MapConfiguracaoEndpoints(); app.MapUsuarioEndpoints(); app.MapApiKeyEndpoints(); app.MapSimuladorEndpoints();`.
- Cada classe `*Endpoints.cs` recebe `this IEndpointRouteBuilder app` e chama `app.MapReadGet(...)`/`app.MapPost(...)`/`app.MapPut(...)`/`app.MapGet(...)` com **paths absolutos literais** (ex.: `TituloEndpoints.cs:17` `"/titulos"`, `ConfiguracaoEndpoints.cs:13` `"/configuracoes/tributos"`, `UsuarioEndpoints.cs:15` `"/admin/usuarios/sync"`, `ApiKeyEndpoints.cs:14` `"/me/keys"`, `ImportacaoEndpoints.cs:12` `"/importacao"`). **Nenhum `MapGroup` é usado hoje.**
- `MapReadGet` (`API/Http/ReadEndpointExtensions.cs:8-20`) é a única indireção: registra `OPTIONS` + os métodos de leitura com dois filtros (`SuppressHeadBodyFilter`, `ConditionalGetFilter`). Ela também assina `this IEndpointRouteBuilder app`.
- **Onde entra o `/v1`:** como todo `Map*Endpoints` e o próprio `MapReadGet` só exigem `IEndpointRouteBuilder`, e `RouteGroupBuilder` (retorno de `app.MapGroup(prefix)`) **implementa** essa interface, basta em `Program.cs` trocar `app.MapXxxEndpoints()` por `v1.MapXxxEndpoints()` onde `var v1 = app.MapGroup("/v1");` — **sem tocar nenhum arquivo de `Endpoints/`**. Rotas com constraint (`TituloEndpoints.cs:13`, `{codigo:regex(...)}`) funcionam normalmente dentro de um grupo — a constraint é por rota, o grupo só adiciona um prefixo ao template.
- Rotas que **não** entram no grupo (ficam como estão, direto no `app`): `/health`, `/health/ready`, `/health/live` (`Program.cs:41-43`), `/` (`:44`), `/metrics` (`:51`, via `app.MapMetrics()`), e `/_test/throw` só em `Testing` (`:55-56`). Ver §4.4.

### 2.2 Geração de hrefs — três mecanismos distintos (o achado principal desta seção)

A hipótese de partida ("todo href sai de `LinkGenerator`, então `/v1` propaga sozinho") é **só parcialmente verdadeira**. Há três padrões diferentes no código hoje:

**(a) `_links` HATEOAS — via `LinkGenerator`, propaga sozinho.**
`API/Http/HateoasLinks.cs:46-51` (`ResolveLink`) chama `linkGenerator.GetPathByName(httpContext, routeName, routeValues)`. Os 4 links de `TituloResource` (`self`, `precos`, `preco-atual`, `simular` — `HateoasLinks.cs:26-33`) resolvem pelo **nome da rota** (`WithName` em `TituloEndpoints.cs:30,50,91,151` e `SimuladorEndpoints.cs:30`), não por string. Como o `RouteGroupBuilder` propaga o prefixo do grupo para o `LinkGenerator` (comportamento padrão do ASP.NET Core desde 6.0), **envolver os endpoints num `MapGroup("/v1")` é suficiente** — zero linha muda em `HateoasLinks.cs`.

**(b) `Location` do `201 Created` — inconsistente entre os dois `POST`s que criam recurso.**
- `ConfiguracaoEndpoints.cs:80`: `Results.CreatedAtRoute("GetTributo", new { id }, new { Id = id })` — via rota nomeada, **propaga sozinho** (mesmo mecanismo do item a).
- `ApiKeyEndpoints.cs:43`: `Results.Created($"/me/keys/{dto.Id}", dto)` — **string interpolada, hardcoded**. Esta chamada **não** propaga com o `MapGroup`; precisa virar `/v1/me/keys/{dto.Id}` à mão (ou, melhor, ser reescrita para `CreatedAtRoute("GetMinhasApiKeys", ...)` — mas essa rota lista, não busca por id; não há uma rota nomeada de "buscar 1 key por id" hoje, então a correção mínima é trocar a string por `$"/v1/me/keys/{dto.Id}"` ou introduzir uma rota nomeada dedicada).

**(c) `Link` header de paginação (RFC 5988) — também hardcoded, também não propaga.**
`TituloEndpoints.cs:163-186` (`BuildPaginationLinkHeader`) monta os 4 rels (`first`/`prev`/`next`/`last`) com `string Href(int targetPage) => $"/titulos/{codigo}/precos?page={targetPage}&pageSize={effectivePageSize}";` (`:169`) — string interpolada, **não** usa `LinkGenerator`. Precisa de correção manual na migração. Como a rota `GetPrecosPorCodigo` (`TituloEndpoints.cs:129`) tem os parâmetros extras (`page`, `pageSize`) que **não** fazem parte do template da rota, `LinkGenerator.GetPathByName(httpContext, "GetPrecosPorCodigo", new { codigo, page = targetPage, pageSize = effectivePageSize })` os anexa automaticamente como query string — dá para eliminar a string hardcoded neste ponto e prevenir o mesmo problema numa v2 futura (registrado como parte da fatia de execução, §7).

**Conclusão da seção:** a premissa "hrefs nascem versionados de graça" vale para os **4 `_links`** e para **1 dos 2** `Location` de `201`. Há **2 pontos hardcoded reais** (`ApiKeyEndpoints.cs:43` e `TituloEndpoints.cs:169`) que exigem edição manual — pequenos, mas é preciso registrá-los para não escapar na migração.

### 2.3 Consumidor: `TesouroApiClient` e as páginas Blazor

`TesouroApiClient` (`Web/Services/TesouroApiClient.cs:16`) expõe `GetAsync`/`GetPagedAsync`/`PostAsync`/`PutAsync` recebendo uma **URI relativa** (`relativeUri`, ex.: `:27,33,45,54`), combinada em runtime com `HttpClient.BaseAddress`. O `BaseAddress` vem de `ApiSettings:BaseUrl` (`Web/Program.cs:23-25`), configurado **sem path** — `Web/appsettings.json:10` `"http://localhost:5000"`; `docker-compose.yml:54`/`docker-compose.e2e.yml:47` `ApiSettings__BaseUrl=http://app:8080`.

Levantamento completo dos call-sites com URI **literal** (não vinda de `_links`/`Link` header — esses navegam sozinhos, ver §2.2a):

| Arquivo:linha | Chamada | Barra inicial? |
|---|---|---|
| `Components/Pages/Simulador.razor:224` | `GetAsync<...>("titulos?vencido=false")` | não |
| `Components/Pages/Cenarios.razor:183` | `GetAsync<...>("titulos?vencido=false")` | não |
| `Components/Pages/Cenarios.razor:236` | `PostAsync<...>("simulador/cenarios", request)` | não |
| `Components/Pages/Titulos.razor:99` | `url = "titulos"` (+ query string montada depois) | não |
| `Components/Pages/Historico.razor:126` | `GetAsync<...>("titulos")` | não |
| `Components/Pages/Tributos.razor:225` | `GetAsync<...>("configuracoes/tributos")` | não |
| `Components/Pages/Tributos.razor:295` | `PostAsync<...>("configuracoes/tributos", body)` | não |
| `Components/Pages/Tributos.razor:300` | `PutAsync<...>($"configuracoes/tributos/{editandoId}", body)` | não |
| `Components/Pages/Admin.razor:86` | `GetAsync<...>("/admin/usuarios?pendentes=true", sub)` | **sim** |
| `Components/Pages/Admin.razor:103` | `PostAsync<...>($"/admin/usuarios/{sub}/aprovar", ..., sub)` | **sim** |
| `Components/Pages/Admin.razor:121` | `PostAsync<...>($"/admin/usuarios/{sub}/desativar", ..., sub)` | **sim** |
| `Components/Pages/ApiKeys.razor:140` | `GetAsync<...>("/me/keys", sub)` | **sim** |
| `Components/Pages/ApiKeys.razor:164` | `PostAsync<...>("/me/keys", ..., sub)` | **sim** |
| `Components/Pages/ApiKeys.razor:188` | `PostAsync<...>($"/me/keys/{id}/revogar", ..., sub)` | **sim** |

**15º call-site — fora das páginas, no fluxo de login (achado do revisor, confirmado por leitura direta):**

| Arquivo:linha | Chamada | Barra inicial? |
|---|---|---|
| `Web/Services/TesouroApiClient.cs:64` | `SyncUsuarioAsync(request) => PostAsync<UsuarioSyncResult>("/admin/usuarios/sync", request)` | **sim** |

Chamado por `Web/Services/GoogleLoginService.cs:21` (`ProcessLoginAsync` → `apiClient.SyncUsuarioAsync(request)`), que por sua vez é invocado em **todo login** a partir de `Web/Program.cs:60,68` (`options.Events.OnCreatingTicket`, resolve `GoogleLoginService` via DI e chama `ProcessLoginAsync`). Diferente dos outros 14, este não é um `.razor` — é um método dedicado (`SyncUsuarioAsync`) dentro do próprio `TesouroApiClient`, então não aparece numa varredura só de `Components/Pages/*.razor`.

São **15 call-sites** — 14 em **7 páginas** `.razor` (`Simulador`, `Cenarios`, `Titulos`, `Historico`, `Tributos`, `Admin`, `ApiKeys`) mais **1** no fluxo de login (`TesouroApiClient.cs`/`GoogleLoginService.cs`) — todos precisam ganhar o prefixo `v1`. **Isto diverge do que a memória do projeto registrava** (`rest_client_consumption_pattern`/tarefa 40: "só `/titulos` e `simulador/cenarios` são hardcoded, o resto navega por `_links`") — essa nota é de **antes** das tarefas 62/63 (endpoints `/admin/usuarios` e `/me/keys`), que chegaram depois e são **igualmente hardcoded**, sem HATEOAS. A memória estava desatualizada; corrigido aqui por leitura direta.

**Por que o 15º call-site é o achado mais perigoso dos três:** o mock de teste que exercita esse caminho — `tests/TesouroDireto.Web.Tests/GoogleLoginServiceTests.cs:33,61` (`.When(HttpMethod.Post, "admin/usuarios/sync", ...)`) — casa contra `FakeHttpMessageHandler.cs:21,25`, que compara `request.RequestUri.AbsolutePath.Trim('/')` contra o padrão registrado (também `Trim('/')`). Como a comparação ignora barras, o mock **continua batendo** mesmo depois de o código de produção migrar para `/v1/admin/usuarios/sync` — o teste fica **verde** sem cobrir a mudança. Sem correção deliberada do mock (§7, fatia 70), esse é o único dos 15 pontos que **não tem rede de segurança nenhuma** (nem bUnit acusa, e o E2E não cobre login — ver §2.4): login quebraria em produção pós-deploy sem nada no plano acusar antes.

**Achado técnico que muda a recomendação de migração (§4.2):** a lista acima mistura URIs **com** e **sem** barra inicial. Hoje isso não importa porque `BaseAddress` não tem path (`http://app:8080` — path vazio). Mas se a migração tentar o atalho "só mudar `ApiSettings:BaseUrl` para terminar em `/v1/`", ele **quebra pela metade**: pelas regras de combinação de `Uri` do .NET, uma URI relativa **com barra inicial** (`/admin/usuarios?...`) **substitui todo o path da base**, descartando o `/v1` — só as chamadas **sem** barra inicial ficariam corretas. Ver §4.2.

### 2.4 E2E

- `tests/TesouroDireto.E2E.Tests/tests/health.spec.ts:4,13` chama `request.get("/health")` e `request.get("/metrics")` diretamente contra a API (`playwright.config.ts:14-21`, projeto `"api"`, `baseURL: API_URL ?? http://localhost:5000`). Essas duas rotas **ficam fora do `/v1`** (§4.4) — o spec não muda.
- Os demais specs (`cenarios`, `titulos`, `historico`, `simulador`, `tributos` — `playwright.config.ts:22-29`, projeto `"web"`) navegam o **site Blazor** (`baseURL: WEB_URL`) via `page.goto`/seletores — nenhum grep encontrou URL de API hardcoded nesses arquivos (confirmado por leitura de `helpers.ts` inteiro e busca por padrão de URL nos 5 specs). Eles exercitam a API **indiretamente**, através do `TesouroApiClient`; migrando o Web (§2.3) eles continuam verdes sem edição própria.

### 2.5 Borda (nginx) e ETag

- `infra/nginx/tesouro-direto.conf:65-72` (porta 443) e `:156-162` (porta 3080, modo *break-glass*) — `location /api/ { proxy_pass http://127.0.0.1:5000/; ... }`. Um `location` de **prefixo** com `proxy_pass` terminado em `/` **descarta só o `/api/`** e repassa o resto do path verbatim. `https://host/api/v1/titulos` vira `http://127.0.0.1:5000/v1/titulos` **sem qualquer edição no nginx**.
- `location /api/swagger` (`:84-91`,`:174-181`) e `location /api/metrics` (`:93-100`,`:183-190`) são regras **exatas** com `proxy_pass` apontando para `/swagger` e `/metrics` **sem** prefixo `/api` nem versão — como essas rotas ficam fora do `/v1` (§4.4), também não mudam.
- **Conclusão: zero mudança necessária em `infra/nginx/tesouro-direto.conf`**, em qualquer uma das duas estratégias (path ou header) — o nginx é opaco ao path por trás de `/api/` e não inspeciona headers customizados de versão.
- **ETag:** `API/Http/ConditionalGetFilter.cs:60-71` (`ComputeETag`) monta `raw = $"{version}|{request.Method}|{request.Path}|{canonicalQuery}"` (`:66`) e faz SHA-256. **`request.Path` participa do hash.** Confirma a hipótese de partida: adicionar `/v1` muda o `Path` de **toda** rota de leitura que passa por `MapReadGet` (`ReadEndpointExtensions.cs:19`, filtro `ConditionalGetFilter`), logo **todo ETag em cache no cliente vira miss uma única vez** no deploy da migração — próxima leitura recomputa e resalva. **Consequência aceita, não um bug**: o pior efeito é um 200 a mais em vez de um 304, não perda de dado nem erro.

---

## 3. O que a auth (tarefas 59-67) já revela sobre este ADR

`ADR-auth.md:292-294` (§7, "Nota sobre versionamento") registrou a dependência: **se** `/v1` viesse antes, `/admin/*`/`/me/keys` nasceriam versionados. Na prática, a execução (**59-67**, `docs/PLANO.md:739-813`) **já rodou e fechou** (todas marcadas `✅ Concluída`, a última — 67 — em 2026-08-08) **sem** `/v1`. Portanto essas rotas **hoje** vivem sem prefixo, junto com as de negócio, e entram no mesmo lote de migração (§7).

---

## 4. Decisão de arquitetura

### 4.1 Estratégia: `/v1` no path vs. header de versão

**Recomendação: prefixo no path (`/v1/...`).** Comparação concreta neste código, não genérica:

| Critério | Path `/v1/titulos` | Header (`Accept`/`Api-Version`) |
|---|---|---|
| nginx | Zero mudança — `location /api/ { proxy_pass .../ ; }` (`tesouro-direto.conf:65-72`) repassa qualquer path após `/api/` verbatim. | Zero mudança também (nginx é opaco a headers de app) — **não é diferenciador**. |
| `_links`/`Location`/`Link` header | Propaga de graça nos 4 `_links` + 1 `Location` via `LinkGenerator`+`MapGroup` (§2.2a-b); só 2 pontos hardcoded a corrigir (§2.2b-c). | **Mesmos** 2 pontos hardcoded a corrigir, **mais** os 4+1 que hoje propagam de graça passariam a exigir lógica de negociação explícita (o `LinkGenerator` não conhece "versão negociada por header" — teria que injetar o header manualmente em cada resposta). Path é estritamente menos trabalho aqui. |
| ETag (`ConditionalGetFilter.cs:66`) | `request.Path` já entra no hash — **zero linha muda** no filtro; a invalidação única (§2.5) acontece de graça e corretamente (v1 nunca compartilha ETag com uma v2 futura, porque o path é diferente). | O header negociado **não** entra no hash hoje — sem uma edição explícita em `ComputeETag` para incluir a versão negociada, uma v2 futura colidiria de ETag com a v1 para o mesmo path/query (bug de correção, não só de código a mais). |
| Framework/tooling | `MapGroup("/v1")` é built-in do Minimal API (.NET 8), zero pacote novo. Swashbuckle já lida com path-versioning como caso comum (`AddSwaggerGen`, `DependencyInjection.cs:38-59`); "Try it out" do Swagger UI mostra a URL completa e funciona sem configurar nada a mais. | Exigiria um pacote de versionamento por header (`Asp.Versioning.Http` ou middleware próprio) — dependência nova, mais superfície. Swagger UI não testa headers de versão por padrão sem `OperationFilter` customizado. |
| curl-abilidade / operação | A versão é visível e fixável em qualquer chamada manual, log de acesso, ou script de smoke test — sem precisar lembrar de setar um header. | Requer disciplina de sempre setar o header certo; erro silencioso (esquecer o header) costuma cair num default ambíguo. |
| Único consumidor hoje é o próprio Web (BFF) | Não precisa de negociação de conteúdo — o cenário "mesma URL serve v1 e v2 a clientes diferentes" (a vantagem clássica de header-versioning) não tem uso real aqui. | Paga o custo de flexibilidade que ninguém está pedindo hoje. |

Não há empate real neste ponto — todos os critérios concretos deste código apontam para path.

### 4.2 Migração sem quebrar o Web: **big-bang no mesmo lote**, não alias temporário

**Recomendação: mover API + Web (+ o pequeno ajuste do E2E, se algum) no mesmo lote de deploy**, sem manter as duas rotas (`/titulos` e `/v1/titulos`) vivas em paralelo.

Motivos concretos:
- API e Web já sobem juntos pelo mesmo `docker-compose.yml`/pipeline (memória `deploy_reset_hard_not_pull`; `docker_rebuild_before_testing`) — não há hoje um cliente externo real cujo *rollout* seja independente do deploy deste repo (ver ressalva abaixo).
- Manter rota dupla exige **nomes de rota únicos** por versão (`WithName` não pode repetir — `TituloEndpoints.cs:30` etc.) e duplica a superfície do Swagger (`SwaggerDoc`), do rate limiter e do `ExcludedPaths` — complexidade real por um benefício que, sem cliente externo hoje, não se paga.
- O `TesouroApiClient`/`GoogleLoginService` (§2.3) têm **15 call-sites hardcoded** que de qualquer forma precisam de edição manual — não há atalho de configuração (`BaseUrl`) que resolva sozinho, dado o achado da barra inicial inconsistente (§2.3). Ou seja, o "custo de migrar o Web" é o mesmo independentemente de manter ou não uma rota-ponte na API; a rota-ponte só adicionaria trabalho **do lado da API**, sem reduzir o trabalho do lado do Web.
- **Correção recomendada junto com a migração:** dado o achado da barra inicial, a forma mais segura de tocar os 15 call-sites é **prefixar cada string literal com `v1/`** (sem barra inicial em nenhuma, por consistência) e manter `ApiSettings:BaseUrl` **sem** path (como é hoje) — evita de vez o efeito colateral de combinação de `Uri` (§2.3), em vez de tentar colocar `/v1` no `BaseUrl` e depender de todo call-site nunca ter barra inicial (frágil a regressão futura). Isso inclui o call-site do login (`TesouroApiClient.cs:64`) — não é opcional: é o único dos 15 sem teste que acusa a omissão (ver §2.3).

**R1 RESOLVIDO — big-bang confirmado (dono, 2026-08-08):** não há cliente externo integrado contra a API sem versão (coerente com o self-service de key só existir desde a tarefa 63, `docs/PLANO.md:773`, 2026-08-07). A premissa de big-bang se sustenta e esta é a estratégia decidida. O raciocínio da alternativa fica registrado como rastro: se um cliente externo passar a consumir a API **antes** de 69-71 serem executadas, revisar esta decisão — nesse caso a resposta muda para alias temporário com janela de depreciação (ver §4.5 e §5, R1).

### 4.3 `_links` já nascem versionados

Com `app.MapGroup("/v1")` envolvendo os `Map*Endpoints()` em `Program.cs` (§2.1):

- **Nenhuma mudança em `HateoasLinks.cs`** — os 4 `_links` (`self`, `precos`, `preco-atual`, `simular`) resolvem via `LinkGenerator.GetPathByName` e herdam o prefixo do grupo automaticamente.
- **`ConfiguracaoEndpoints.cs:80`** (`Location` do `POST /configuracoes/tributos`, via `CreatedAtRoute("GetTributo", ...)`) — também de graça.
- **`ApiKeyEndpoints.cs:43`** (`Location` do `POST /me/keys`) — precisa de edição manual: trocar `Results.Created($"/me/keys/{dto.Id}", dto)` por `Results.Created($"/v1/me/keys/{dto.Id}", dto)`. Registrado como parte da fatia de execução (§7); recomendado, mas fora do escopo mínimo, migrar para um padrão consistente com o resto (nomear uma rota `"GetMinhaApiKeyPorId"` ainda que sem handler de leitura direta por id, só para o `CreatedAtRoute` funcionar) — decisão de estilo, não bloqueia a migração.
- **`TituloEndpoints.cs:169`** (`Link` header de paginação) — precisa de edição manual (§2.2c); a correção recomendada é trocar a interpolação por `LinkGenerator.GetPathByName(httpContext, "GetPrecosPorCodigo", new { codigo, page = targetPage, pageSize = effectivePageSize })`, que já resolve o prefixo **e** elimina de vez o hardcode (proteção para uma v2 futura).

### 4.4 Fora do versionamento: `/health`, `/health/ready`, `/health/live`, `/metrics`, `/swagger`

**Ficam onde estão, sem `/v1`.** Justificativa:

- São contratos de **infraestrutura/operação**, não do domínio de negócio — consumidos por Docker healthcheck, o gate de readiness do compose, o scrape do Prometheus (`prometheus.yml`, alvo interno `app:8080`, ver nota da tarefa 43 em `docs/PLANO.md:597`) e o nginx (`location /api/swagger`, `location /api/metrics`, `tesouro-direto.conf:84-100,174-190`). Versionar rota de healthcheck não é prática comum (Kubernetes, Docker Compose e a maioria dos frameworks tratam `/health`/`/metrics` como estáveis e fora do contrato versionado da API).
- `ApiKeyMiddleware.ExcludedPaths` (`appsettings.json:18`, `["/health","/metrics","/swagger"]`) casa por `StartsWithSegments` (`ApiKeyMiddleware.cs:170-171`) contra o **literal configurado** — deixando essas rotas fora do grupo `/v1`, essa config **não precisa mudar**. Se elas fossem versionadas, essa lista (e o healthcheck do Docker, e o scrape do Prometheus, e as duas regras do nginx) teriam que mudar em uníssono — custo real sem benefício, já que não há "v2 do healthcheck".
- O `SwaggerDoc("v1", ...)` em `DependencyInjection.cs:40` é o **nome do documento OpenAPI** do Swashbuckle — uma string interna da lib, hoje coincidentemente `"v1"`, **sem relação** com o prefixo de URL `/v1`. Registrar a coincidência para não confundir um futuro leitor: renomear o prefixo de URL não obriga renomear o `SwaggerDoc`, e vice-versa. Nenhuma ação necessária agora.

### 4.5 Sequência vs. auth (tarefa 56 / fatias 59-67)

**Os dois lados**, como pedido, mesmo com o resultado já consumado (§3):

- **Lado "antes"** (`/v1` primeiro, depois auth): as rotas de gestão (`/admin/*`, `/me/keys`) nasceriam versionadas desde o primeiro commit, nunca precisando de uma migração própria depois. Custo: acopla dois refactors grandes e ortogonais no mesmo período — um versionamento transversal (toca 6 arquivos de `Endpoints/` + 7 páginas Blazor + o fluxo de login) simultâneo a 9 fatias de auth de alto risco de segurança (`ApiKeyMiddleware` v2, rate limit, OAuth) que passaram por ciclos adversariais pesados de revisor (`docs/PLANO.md:758,767,776,805,808` registram vários furos pegos e corrigidos). Misturar as duas coisas dificultaria isolar a causa de uma regressão.
- **Lado "depois"** (auth primeiro, `/v1` depois — **o que de fato aconteceu**): cada fatia de auth foi revisada e testada isoladamente, sem o ruído de um prefixo de rota mudando por baixo. O versionamento vira, agora, um **passe mecânico único** que toca rotas de negócio e de gestão de uma vez só (mesma fatia, §7), porque de qualquer forma nenhuma delas tinha `/v1` até aqui.

**Recomendação: manter a sequência "depois"** (endossar o que já ocorreu), pelos motivos acima — e porque não há mais escolha real: reabrir 59-67 só para prefixá-las teria o mesmo custo de fazer isso agora, junto com o resto, sem o benefício de tê-las nascido versionadas.

**Essa recomendação herdava a condição de R1 (§5), agora RESOLVIDA "não" (dono, 2026-08-08):** como não há cliente externo integrado, a premissa de §4.2 (big-bang) se sustenta e "depois" é indolor — a migração de `/admin/*`/`/me/keys` é um passe mecânico dentro do big-bang. Registro do rastro, caso R1 mude no futuro: se passar a existir cliente externo integrado, a migração dessas rotas passaria a exigir a mesma janela de depreciação do resto do contrato (§4.2) — nesse cenário o custo de "depois" subiria, mas ainda **não** ficaria pior que "antes" teria ficado. A recomendação de sequência é robusta ao resultado de R1; só a de **migração** (§4.2) dependia dele — e R1 fechou a favor de big-bang.

---

## 5. Riscos e pontos que exigem aval

- **R1 — RESOLVIDO "não" (dono, 2026-08-08): não há cliente externo integrado contra a API sem versão.** Big-bang (§4.2) é seguro e é a estratégia decidida. Rastro: era o único ponto que exigia aval de operação/produto (não decidível por leitura de código); se um cliente externo passar a consumir a API antes de 69-71 rodarem, reabrir esta decisão (a resposta viraria rota-ponte temporária com prazo de depreciação anunciado).
- **R2 — Local exato do `MapGroup`.** Registrar em `Program.cs` (não em cada `*Endpoints.cs`) mantém a decisão de prefixo num único lugar e não obriga a assinatura de nenhum `Map*Endpoints` a mudar (todas já recebem `IEndpointRouteBuilder`). Baixo risco, mas registrar para não haver dúvida na hora de codar: `var v1 = app.MapGroup("/v1"); v1.MapTituloEndpoints(); ...` substitui as 6 linhas de `Program.cs:45-50`.
- **R3 — Os 2 hrefs hardcoded (`ApiKeyEndpoints.cs:43`, `TituloEndpoints.cs:169`) são o único jeito de esquecer alguma coisa nesta migração.** Uma varredura `grep -n '"/.*"' src/TesouroDireto.API/Endpoints/*.cs` antes do PR final é a rede de segurança recomendada na fatia de execução (§7) — não confiar só na lista desta seção envelhecer bem.

---

## 6. Alternativas descartadas

| Alternativa | Por que descartada |
|---|---|
| Versionamento por header/`Accept` (`Asp.Versioning.Http` ou custom) | Ver comparação completa em §4.1 — perde em todos os critérios concretos deste código; resolve um problema (múltiplos clientes na mesma URL) que não existe hoje. |
| Rota dupla permanente (`/titulos` e `/v1/titulos` convivendo indefinidamente) | Sem um 2º cliente real hoje, não há quem dependa da rota antiga além do próprio Web, que migra no mesmo PR. Duplicar rota também duplica nome (`WithName`), Swagger, rate limit e `ExcludedPaths` — custo permanente por um benefício transitório. |
| Versionar também `/health`/`/metrics`/`/swagger` | Quebraria o healthcheck do Docker, o scrape do Prometheus e as 2 regras exatas do nginx (`tesouro-direto.conf:84-100,174-190`) por nenhum ganho — não existe "v2 de healthcheck". Ver §4.4. |
| Renomear o `SwaggerDoc` de `"v1"` para acompanhar o prefixo de URL | São conceitos independentes (nome interno do Swashbuckle vs. prefixo de rota); forçar o acoplamento cria trabalho a cada versão nova sem necessidade. Ver §4.4. |

---

## 7. Fatias de execução (formato `PLANO.md`)

**Nota de numeração:** a orientação original apontava "a partir de 58", mas o `PLANO.md` já tem a tarefa **58 ocupada** (carga k6, `docs/PLANO.md:725-731`) e as tarefas **59-68** já preenchidas (auth 59-67 concluídas + 68 pendente, `docs/PLANO.md:739-820`). Numerar a partir de 58 colidiria com a k6. A numeração real, verificada no arquivo, começa em **69**.

Dependências: **69** é a base (toca só a API); **70** depende de **69** (o Web precisa saber o path novo antes de compilar/rodar contra ele); **71** depende de **69+70** (é a verificação de ponta a ponta do conjunto). Recomendado como **um único PR** cobrindo 69+70 (big-bang, §4.2), com 71 como checklist de aceite do mesmo PR — mas numerados em separado para rastreio, no padrão já usado pela 59-67.

### 69. `/v1` no path da API + saneamento dos hrefs hardcoded 🟡
- **Escopo:** envolver o registro de rotas de negócio/gestão em `app.MapGroup("/v1")` (`Program.cs:45-50`), preservando `/health*`, `/`, `/metrics` e `/_test/throw` fora do grupo (§4.4). Corrigir os 2 hrefs hardcoded: `ApiKeyEndpoints.cs:43` (`Location` do `POST /me/keys`) e `TituloEndpoints.cs:169` (`Link` header de paginação, trocar por `LinkGenerator.GetPathByName`). Rodar a varredura de rede de segurança (R3, §6) antes do PR.
- **Arquivos:** `API/Program.cs`, `API/Endpoints/ApiKeyEndpoints.cs`, `API/Endpoints/TituloEndpoints.cs`. Nenhum outro `*Endpoints.cs` muda de conteúdo (só passam a ser registrados no grupo).
- **Risco:** baixo-médio — mecânico, mas é o tipo de mudança que quebra silenciosamente se um href escapar. Mitigado pela varredura + suíte de integração HTTP existente (que hoje assume paths sem `/v1` e vai acusar 404 em qualquer rota esquecida fora do grupo, ou o contrário).
- **Verificação:** suíte de integração HTTP inteira passando com os paths novos. **Não existe uma classe central de rotas nos testes** — os paths são strings literais espalhadas em ~29 arquivos de `tests/`; achar todos com `grep -rl '"/titulos\|"/admin\|"/me/keys\|"/configuracoes\|"/simulador\|"/importacao' tests/` e prefixar cada um com `/v1` (rotas de `/health*`/`/metrics`/`/swagger` ficam de fora do grep por não começarem com nenhum desses prefixos — conferir que o grep não os pegou por engano). `GET /v1/titulos/{codigo}` traz `_links` com hrefs `/v1/...`; `POST /v1/me/keys` responde `Location: /v1/me/keys/{id}`; `GET /v1/titulos/{codigo}/precos?page=1` traz `Link` header com `/v1/...`; `/health`, `/metrics`, `/swagger` continuam **sem** `/v1` e sem exigir `X-Api-Key` (`ExcludedPaths` intacto).

### 70. Web consome `/v1` 🟡
- **Escopo:** prefixar os **15 call-sites hardcoded** levantados em §2.3 com `v1/` (sem barra inicial, uniformizando as que hoje têm barra — ver o risco de combinação de `Uri` em §2.3/§4.2): os 14 em `Web/Components/Pages/*.razor` **mais** `TesouroApiClient.cs:64` (`SyncUsuarioAsync`, usado no login por `GoogleLoginService.cs:21`). **Não** mexer em `ApiSettings:BaseUrl` (continua sem path). As chamadas que navegam por `_links`/`Link` header (ex.: `simularHref` em `Simulador.razor:271`, `precosLink.Href` em `Historico.razor:169`) **não mudam** — já vêm versionadas da API (69). **Obrigatório:** atualizar o mock de `tests/TesouroDireto.Web.Tests/GoogleLoginServiceTests.cs:33,61` de `"admin/usuarios/sync"` para `"v1/admin/usuarios/sync"` **antes** de tocar `TesouroApiClient.cs:64` — hoje esse teste bate por `AbsolutePath.Trim('/')` (`FakeHttpMessageHandler.cs:21,25`) e **não acusa** se o prefixo faltar (§2.3); só depois de o mock virar `v1/admin/usuarios/sync` é que ele passa a falhar caso `TesouroApiClient.cs:64` não seja migrado, fechando a lacuna que o revisor apontou.
- **Arquivos:** `Web/Components/Pages/{Simulador,Cenarios,Titulos,Historico,Tributos,Admin,ApiKeys}.razor` (as linhas exatas da tabela de §2.3) + `Web/Services/TesouroApiClient.cs` (linha 64, `SyncUsuarioAsync`) + `tests/TesouroDireto.Web.Tests/GoogleLoginServiceTests.cs` (mock, linhas 33 e 61). `GoogleLoginService.cs` em si **não muda** — só chama `SyncUsuarioAsync()`, que já encapsula a URI.
- **Risco:** baixo — mecânico, cada call-site é uma string literal. O risco real é esquecer um dos 15 (o de `TesouroApiClient.cs:64` é o único sem rede de segurança própria hoje — §2.3); mitigado por `dotnet build` (não pega, são strings), pelo mock corrigido de `GoogleLoginServiceTests` e pelo bUnit/E2E da §71.
- **Verificação:** bUnit de todas as páginas + `GoogleLoginServiceTests` continuam verdes **com o mock já em `v1/...`** (reverter a migração em `TesouroApiClient.cs:64` deve deixar `GoogleLoginServiceTests` vermelho — é a prova de não-vacuidade deste passo). Verificação manual falsificável das 7 páginas: abrir cada uma logado como usuário **aprovado** (Titulos, Historico, Tributos, Simulador, Cenarios sem login; Admin e ApiKeys exigem login+aprovação) e, via DevTools → Network, confirmar que **toda** requisição para a API sai com `/v1/` no path — qualquer chamada ainda sem `/v1` bate 404 na API migrada (69) e a página correspondente deve exibir o erro de `ApiResult<T>.IsSuccess=false` (não uma tela em branco silenciosa). Login via Google (ou o `TestAuthenticationHandler`, `webappfactory_needs_db_config`) confirmado à parte na §71.

### 71. Verificação de ponta a ponta pós-`/v1` 🟢
- **Escopo:** rodar a suíte E2E completa (Playwright, `run-e2e.sh`) contra API+Web migrados; conferir manualmente o Swagger UI (`/api/swagger` via túnel, tarefa 37) mostrando as rotas sob `/v1`; smoke test do nginx (`curl` via `/api/v1/titulos` e via `/api/health`) confirmando que a borda não precisou de deploy (§2.5); confirmar que o primeiro request de leitura após o deploy vem sem `If-None-Match` reconhecido (miss esperado do ETag, §2.5) e o segundo já bate 304. **Explícito para o fluxo de login/OAuth (achado do 15º call-site, §2.3):** smoke manual do login Google fim-a-fim (ou via `TestAuthenticationHandler`) confirmando que `POST /v1/admin/usuarios/sync` responde 2xx durante o `OnCreatingTicket`; repetir o mesmo smoke **sem** o prefixo aplicado num ambiente de controle (ou revertendo só `TesouroApiClient.cs:64`) para confirmar que o login quebra com 404 quando o call-site não é migrado — prova de que a lacuna que o revisor achou (E2E não cobre login, bUnit só cobre com o mock já corrigido em 70) fica coberta neste passo antes do deploy real.
- **Arquivos:** nenhum arquivo de produção — só execução/observação. Se algum spec E2E precisar de ajuste (nenhum encontrado em `tests/TesouroDireto.E2E.Tests/tests/*.spec.ts` hoje, §2.4), entra aqui.
- **Risco:** baixo — é o gate de aceite, não introduz mudança nova.
- **Verificação:** `health.spec.ts` continua verde sem edição (`/health`,`/metrics` fora do `/v1`); os 5 specs "web" verdes (navegam a UI, indiferentes ao path da API); smoke manual do nginx documentado no PR; smoke manual do login (`POST /v1/admin/usuarios/sync` 2xx) documentado no PR.

---

## 8. Critério de aceite deste ADR

Um leitor que nunca viu o repo consegue implementar `/v1` só com este documento: onde entra o `MapGroup` (§2.1/§7-69), quais dos 6 hrefs propagam sozinhos e quais 2 precisam de edição manual com a linha exata (§2.2/§4.3), quais 15 call-sites do Web (14 em 7 páginas `.razor` + 1 no fluxo de login) precisam de prefixo, por que não usar `BaseUrl`, e por que o mock de `GoogleLoginServiceTests` precisa mudar de propósito para virar rede de segurança de verdade (§2.3/§4.2/§7-70), o que fica fora e por quê (§4.4), e a consequência aceita do ETag (§2.5) — sem precisar abrir uma decisão de design nova.
