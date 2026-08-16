# Teste de carga (k6)

Ferramenta de teste de carga sob demanda para a API e para o site (Blazor Server).
Roda fora da pipeline (ver seção 8) e nunca aponta para produção por padrão.

Scripts em [`tests/load/`](../../tests/load/), orquestrador [`run-load.sh`](../../run-load.sh)
na raiz, overlay [`docker-compose.load.yml`](../../docker-compose.load.yml) e dashboard
[`infra/grafana/dashboards/load-test.json`](../../infra/grafana/dashboards/load-test.json).

Para isolar a **causa** do joelho de capacidade observado no baseline (seção 7 abaixo) —
CPU vs thread-pool starvation vs GC vs Postgres — ver [`profiling.md`](profiling.md)
(`tests/load/profiling/`, overlay `docker-compose.profiling.yml`).

## 1. Objetivo e escopo

Cada cenário mede uma coisa diferente:

| Script | O que mede | Unidade |
|---|---|---|
| `tests/load/api/titulos.js` | `GET /v1/titulos` sob carga + eficácia do ETag (2ª chamada com `If-None-Match` deve voltar 304) | req/s, latência p95/p99, taxa de erro, taxa de 304 |
| `tests/load/api/preco-atual.js` | `GET /v1/titulos/{codigo}/preco-atual` sob carga | req/s, latência p95/p99, taxa de erro |
| `tests/load/api/historico.js` | `GET /v1/titulos/{codigo}/precos` (paginado, `page`/`pageSize`) sob carga, valida presença de `X-Total-Count` e `Link` | req/s, latência p95/p99, taxa de erro |
| `tests/load/api/simulador.js` | `POST /v1/simulador` sob carga (fluxo de escrita/cálculo, mais custoso que os GETs) | req/s, latência p95/p99, taxa de erro |
| `tests/load/rate-limit/validate.js` | **Não é teste de capacidade.** Confirma que o rate limit por cliente (60/min, ver tarefa 66) dispara `429` com `Retry-After` | taxa de respostas 429 (`got_429`) |
| `tests/load/site/circuits.js` | Site Blazor Server: quantos **circuitos SignalR concorrentes** o servidor sustenta segurando WebSockets abertos | **circuitos concorrentes (Gauge), NÃO req/s** |

**Importante — deixar explícito:** o cenário do site (`site/circuits.js`) **não mede requisições
por segundo**. Blazor Server mantém estado por circuito num WebSocket persistente; a métrica
relevante é quantas conexões/circuitos o servidor consegue manter abertos e responsivos ao
mesmo tempo (concorrência), não throughput de HTTP. Cada VU do k6 abre e segura 1 circuito
por `HOLD_DURATION_MS` (75s), enviando pings periódicos (`PING_INTERVAL_MS`, 15s) — não dispara
requisições repetidas.

## 2. Pré-requisitos

- **k6** instalado localmente: `brew install k6` (validado nesta máquina com `k6 v2.1.0`).
  Alternativa sem instalar: usar o serviço `k6` do overlay (`docker compose ... run --rm k6 ...`,
  ver seção 3).
- **Docker** — necessário para subir a stack local (Prometheus com remote-write habilitado,
  ver `docker-compose.load.yml`) e/ou o próprio ambiente sob teste (API/Web/Postgres).
- **Ambiente de teste com dados reais de títulos/preços** — os scripts de API (exceto
  `titulos.js`) chamam `GET /v1/titulos?vencido=false` uma única vez, no `setup()` do k6, e
  reusam o `codigo` do primeiro título **não vencido** retornado (`pickCodigo()` em
  `tests/load/lib/http.js`) em todas as iterações do `flow()`; se a lista filtrada vier vazia,
  o script aborta com erro explícito. Não rode contra um banco vazio nem só com títulos
  vencidos.
- Para `rate-limit/validate.js`: uma **client key de teste semeada no banco** (ver seção 4).

## 3. Como apontar para o ambiente

O alvo é **obrigatório e explícito, sem default**. Todos os scripts chamam `requireEnv(...)`
no início do arquivo (fora de qualquer função) — se a variável faltar, o script aborta
**na fase de init**, antes de disparar qualquer requisição. Rodar sem alvo aborta com uma
mensagem clara pedindo `-e API_URL=...` ou `-e WEB_URL=...`.

Toda URL passa por `assertNaoProd()` (`tests/load/lib/config.js`): se contiver
`dadosdotesourodireto.com.br`, o script aborta a menos que `ALLOW_PROD=1` seja definido
explicitamente. Isso vale tanto para `run-load.sh` quanto para chamadas diretas de `k6 run`.

Variáveis:

- `API_URL` — obrigatória para os 4 fluxos de API (`api/*.js`) e para `rate-limit/validate.js`.
- `WEB_URL` — obrigatória para `site/circuits.js`.
- `API_KEY` — chave de API (service key) usada via header `X-Api-Key` nos fluxos de capacidade.
- `CLIENT_API_KEY` — chave de cliente, usada só por `rate-limit/validate.js` (nunca `API_KEY`).
- `ALLOW_PROD=1` — único jeito de permitir alvo em produção (não recomendado).

### Via `run-load.sh` (recomendado — orquestra o overlay do Prometheus)

`run-load.sh` exige o caminho do script k6 como **1º argumento posicional, sem default**;
sem ele, aborta com uso. Exige também `API_URL` **ou** `WEB_URL`, e `API_KEY`. Com `--local`,
sobe `docker-compose.load.yml` (Prometheus com `--web.enable-remote-write-receiver`) e usa
como destino de métricas `http://localhost:9090/prometheus/api/v1/write` (a menos que
`K6_PROMETHEUS_RW_SERVER_URL` já esteja definida). Sem `--local`, `K6_PROMETHEUS_RW_SERVER_URL`
é obrigatória (sem default). Sempre exporta `K6_PROMETHEUS_RW_TREND_STATS=p(95),p(99),avg,max,min`
para o k6 escrever percentis de latência como séries próprias.

```bash
# fluxos de API (capacidade), local, contra ambiente de dev/staging
API_URL=http://localhost:8080 API_KEY=<service-key> ./run-load.sh --local tests/load/api/titulos.js
API_URL=http://localhost:8080 API_KEY=<service-key> ./run-load.sh --local tests/load/api/preco-atual.js
API_URL=http://localhost:8080 API_KEY=<service-key> ./run-load.sh --local tests/load/api/historico.js
API_URL=http://localhost:8080 API_KEY=<service-key> ./run-load.sh --local tests/load/api/simulador.js

# site (circuitos Blazor)
WEB_URL=http://localhost:8081 API_KEY=<service-key> ./run-load.sh --local tests/load/site/circuits.js
```

(`API_KEY` é exigida pelo `run-load.sh` mesmo para o cenário do site porque o script trata
`API_KEY` como obrigatória de forma incondicional — não é usada por `circuits.js`, mas precisa
estar definida para passar no guard do orquestrador.)

### Via `k6 run` direto (sem o orquestrador)

```bash
# 4 fluxos de API — API_URL e API_KEY obrigatórias
k6 run -o experimental-prometheus-rw \
  -e API_URL=http://localhost:8080 -e API_KEY=<service-key> \
  tests/load/api/titulos.js

k6 run -o experimental-prometheus-rw \
  -e API_URL=http://localhost:8080 -e API_KEY=<service-key> \
  tests/load/api/preco-atual.js

k6 run -o experimental-prometheus-rw \
  -e API_URL=http://localhost:8080 -e API_KEY=<service-key> \
  tests/load/api/historico.js

k6 run -o experimental-prometheus-rw \
  -e API_URL=http://localhost:8080 -e API_KEY=<service-key> \
  tests/load/api/simulador.js

# site — WEB_URL obrigatória (API_KEY não é usada aqui)
k6 run -o experimental-prometheus-rw \
  -e WEB_URL=http://localhost:8081 \
  tests/load/site/circuits.js
```

`-o experimental-prometheus-rw` é o output do k6 confirmado nesta versão (`k6 v2.1.0`) para
escrever via remote-write no Prometheus, lendo o endpoint de `K6_PROMETHEUS_RW_SERVER_URL`
(precisa ser *subpath-aware*, ex.: `http://localhost:9090/prometheus/api/v1/write`, já que o
Prometheus deste projeto roda atrás de `--web.external-url=/prometheus/`). Defina essa variável
antes de rodar `k6 run` direto (o `run-load.sh` faz isso por você com `--local`).

### Os dois scenarios de cada fluxo de API (`smoke` e `ramp`)

Cada um dos 4 scripts em `tests/load/api/` define **dois scenarios no mesmo arquivo**:

- `smoke`: `constant-vus`, 2 VUs, 30s, `exec: "smoke"`.
- `ramp`: `ramping-vus`, 0→10→25→50→100→200 VUs em estágios de 30s/30s/30s/30s/1m/30s,
  `exec: "ramp"`, com `startTime: "35s"`.

Os dois **coexistem no mesmo `options.scenarios`** e o k6 executa **todos os scenarios
definidos** numa única chamada de `k6 run` — não há seleção implícita. Na prática, rodar
`k6 run tests/load/api/titulos.js` executa `smoke` primeiro (0s–30s) e, 5s depois de ele
começar a liberar VUs (`startTime: "35s"`), `ramp` roda por conta própria (mais ~3m30s de
estágios). O run total dura ~4 minutos: `smoke` funciona como aquecimento/sanity check antes
da rampa de carga.

Esta versão do k6 (`v2.1.0`) **não tem uma flag nativa `k6 run --scenario <nome>`** para
isolar um scenario específico (verificado com `k6 run --help`; só existem filtros de tag para
output, não de execução). Parxa rodar *apenas* `smoke` ou *apenas* `ramp` isoladamente, é preciso
editar temporariamente o arquivo e comentar o scenario que não deve rodar — os executores não
implementaram uma seleção via variável de ambiente. Para os runs de capacidade descritos aqui,
isso normalmente não é necessário: deixar os dois coexistirem é o comportamento pretendido.

## 4. Modos de rate limit — nunca no mesmo run

Existem dois modos distintos e **não devem ser misturados no mesmo run**:

### Capacidade (fluxos normais de `api/*.js`)

Usa **`API_KEY`** (service key, sem rate limit por cliente aplicado da mesma forma — objetivo
é achar o teto de capacidade do serviço, não validar o limitador). Os thresholds
(`http_req_duration p(95)<400`, `http_req_failed rate<0.01`) devem seguir batendo conforme a
carga sobe; 429 aqui não é esperado e indicaria o limitador atrapalhando um teste de capacidade
com a chave errada.

### Validar rate limit (`rate-limit/validate.js`)

Usa **`CLIENT_API_KEY`** (nunca `API_KEY`) — dispara ~2 req/s por 35s contra `GET /v1/titulos`,
uma taxa acima do limite de 60/min (tarefa 66), e confirma que o servidor responde `429` com
header `Retry-After` presente (métrica `got_429`, threshold `rate>0`).

```bash
API_URL=http://localhost:8080 API_KEY=<service-key> CLIENT_API_KEY=<client-key-de-teste> \
  ./run-load.sh --local tests/load/rate-limit/validate.js

# ou direto:
k6 run -o experimental-prometheus-rw \
  -e API_URL=http://localhost:8080 -e CLIENT_API_KEY=<client-key-de-teste> \
  tests/load/rate-limit/validate.js
```

**Pré-requisito — semear a client key de teste no banco.** `rate-limit/validate.js` não cria
a chave; ela precisa existir previamente na tabela `api_keys`, seguindo o mesmo padrão usado
nos testes E2E de autenticação (tarefas 59/61/63): uma linha em `api_keys` com o **hash SHA-256**
da chave (`ApiKeyHash`, ver VO de identidade da tarefa 59), papel `Cliente`, vinculada a um
usuário com `ativo = true` (aprovado). Sem isso, toda requisição de `validate.js` volta `401`
em vez de eventualmente `429`, e o teste não valida nada.

## 5. Onde ver as métricas

Desde a tarefa 77 não há mais Grafana local, e o dashboard **"Load Test (k6)"** (uid `load-test-k6`,
`infra/grafana/dashboards/load-test.json`) não vive no Grafana Cloud — e não pode: ele lê do
**Prometheus efêmero local** que só sobe sob `--profile load` (`docker-compose.yml`, serviço
`prometheus`, publicado em `127.0.0.1:9090/prometheus/`), e o backend SaaS do Grafana Cloud não tem
(nem terá) rota para o `127.0.0.1` de quem roda o teste; `scripts/grafana-cloud/apply-cloud.sh`
remove o `load-test-k6` da nuvem de forma idempotente caso ele apareça lá por engano
(`infra/alloy/README.md:52-66`). Sem Grafana local para renderizar o dashboard, o caminho real é
consultar o Prometheus efêmero diretamente: `run-load.sh` imprime, ao final do run, o destino real
dos dados e lembra que não há Grafana neste modo — `http://localhost:9090/prometheus/` em `--local`
(use a UI/API do próprio Prometheus), ou o `K6_PROMETHEUS_RW_SERVER_URL` explícito no outro modo
(`run-load.sh:122-127`).

Painéis: VUs, requisições/s, latência p95/p99, taxa de erro HTTP, taxa de 304 (ETag sob carga,
só aparece rodando `titulos.js`), circuitos SignalR concorrentes (Blazor), e CPU/memória do
host (métricas `node_*`, hoje coletadas pelo Alloy — ver `infra/alloy/config.alloy`, substituiu o
`node-exporter` na 77.1).

## 6. Caveats do cenário Blazor (`site/circuits.js`)

O protocolo usado (handshake SignalR + framing MessagePack) é **interno e não documentado
oficialmente** pelo ASP.NET Core — reimplementá-lo manualmente em k6 é frágil a upgrades do
Blazor. Nível de confiança, transcrito honestamente do que foi validado:

**CERTO (validado contra um servidor WebSocket mock, não contra Blazor real):**
- Handshake SignalR em texto: `` {"protocol":"blazorpack","version":1}\x1e ``.
- Framing binário do protocolo MessagePack do SignalR: length-prefix VarInt de 7 bits
  (`tests/load/lib/msgpack.js`, `frame()`/`encodeVarIntLength()`).
- Forma da mensagem de Invocation: `[1, headers, invocationId, target, args[], streamIds[]]`
  (tipo `1` = Invocation).
- Mensagem de Ping: `[6]` (tipo `6` = Ping).
- Fluxo de `negotiate` (`POST /_blazor/negotiate?negotiateVersion=1`) e uso do
  `connectionToken` retornado para montar a URL do WebSocket (`/_blazor?id=<token>`).

**INCERTO (não validado contra uma instância real de Blazor Server — não havia uma
disponível/saudável nesta sessão para testar ao vivo):**
- A assinatura exata dos argumentos de `StartCircuit`: implementado como
  `StartCircuit(descriptorsJson, applicationState)` (dois argumentos posicionais), a partir da
  leitura do código-fonte do Blazor, mas sem confirmação empírica contra um servidor real.
- O formato exato do comentário HTML com o application state embutido na página: o extrator
  (`extractCircuitStartArgs` em `tests/load/lib/blazor.js`) tenta **3 padrões em fallback**
  (`<!--Blazor-Server-Component-State-->...<!--/Blazor-Server-Component-State-->`,
  `<!--Blazor-Server-Component-State:...-->`, e um genérico de base64 entre comentários) porque
  não há certeza de qual forma o Blazor Server desta versão do .NET realmente emite.
- Se `invocationId: null` é aceito pelo servidor real na mensagem de Invocation do
  `StartCircuit` (o protocolo permite `null` para invocations "fire-and-forget", mas isso não
  foi confirmado contra Blazor Server real).

Rodar `site/circuits.js` pela primeira vez contra um Blazor Server real pode exigir ajustes
nesses pontos. Se o protocolo se mostrar impraticável de manter, o **fallback não implementado**
é usar Playwright (já presente no repositório para os testes E2E) dirigindo N contextos de
navegador reais para simular circuitos — mais pesado por VU, mas sem depender de reimplementar
o protocolo interno.

## 7. Baseline (limites de capacidade observados)

Duas medições contra **produção** (VPS, atrás do nginx+TLS), ramp de 0→200 VUs a partir de
**1 IP** (o número de origem importa — ver a nota sobre a borda). Números do resumo do `k6`
(`--summary-export`); o `site/circuits.js` não foi executado contra prod (segurar circuitos
Blazor exige a validação do handshake contra o servidor real — ver seção 6).

### 7.1 Antes das otimizações (2026-08-08)

| Fluxo | Throughput obs. | p95 | Taxa de erro | Taxa de 304 | Teto atingido |
|---|---|---|---|---|---|
| `api/titulos.js` (GET + ETag) | ~23 req/s | 6,9 s | 0% | 100% | **Capacidade da VPS** (satura em latência) |
| `api/historico.js` (GET paginado) | ~21 req/s | 7,2 s | ~0,01% | N/A | **Capacidade da VPS** (satura em latência) |
| `api/preco-atual.js` (GET) | ~132 req/s¹ | 2,2 s | 82% (429 da borda) | N/A | **Borda nginx** (~30 req/s por IP) |
| `api/simulador.js` (POST) | ~315 req/s¹ | 223 ms | 95% (429 da borda) | N/A | **Borda nginx** (~30 req/s por IP) |
| `site/circuits.js` | não executado contra prod | N/A (mede circuitos concorrentes, não latência) | N/A | N/A | — |

¹ Throughput inflado pelas respostas `429` (que são rápidas): o número alto é a taxa de tentativas,
não de sucesso. O sucesso real (`status 200`) foi ~17% no `preco-atual` e ~5% no `simulador`.

**Os dois tetos distintos da configuração (estado pré-otimização; ver 7.2 para o depois):**

1. **Borda (nginx): ~30 req/s por IP** (`limit_req zone=api rate=30r/s burst=50 nodelay`),
   responde **`429` + `Retry-After: 5`**. Confirmado com uma rajada controlada (70 req → 56×200 /
   14×429). Os fluxos leves por requisição (`preco-atual`, `simulador`) geram tráfego acima disso
   e batem majoritariamente em `429` — comportamento **protetor esperado**, não falha do app.
2. **Capacidade da VPS (CPU/DB):** os fluxos pesados por requisição (`titulos`, com a dupla
   chamada de ETag; `historico`, paginado) **saturam em latência (~7 s de p95) a ~21–23 req/s,
   antes** de atingir o teto da borda — por isso 0% de erro, mas p95 altíssimo. O app **não caiu**
   em nenhum cenário (0 respostas 5xx).

Referência local (não-representativa do hardware da VPS, só para sanidade do script): `titulos.js`
no ambiente local deu p95 262 ms, 0,01% de erro e 304 de 99,97% a 200 VUs (~874 req/s).

### 7.2 Depois das otimizações (2026-08-09) — gargalo de app eliminado

Após deploy das otimizações **#48** (cache da version do ETag) e **#49** (`NoResetOnClose` no
Npgsql), o mesmo teste (ramp 200 VUs, 1 IP). A coluna que importa é a **latência das respostas
`200`** (`expected_response:true`) — o throughput de tentativa e a taxa de erro apenas refletem
que o ramp gera muito acima do teto da borda e a maioria vira `429`:

| Fluxo | p95 dos `200` — antes → depois | Teto atingido agora |
|---|---|---|
| `api/titulos.js` (GET + ETag) | **6,9 s → 0,93 s** | **Borda nginx** (~30 req/s por IP) |
| `api/historico.js` (GET paginado) | **7,2 s → 0,31 s** (~23×) | **Borda nginx** |
| `api/preco-atual.js` (GET) | ~0,3 s → 0,31 s | **Borda nginx** (já era) |
| `api/simulador.js` (POST) | ~0,25 s → 0,24 s | **Borda nginx** (já era) |

**Mudança estrutural:** antes havia **dois tetos** (capacidade da VPS para os pesados, borda para
os leves). Depois sobrou **um só — a borda nginx (~30 req/s por IP)**, para todos os 4 fluxos. Os
fluxos pesados **deixaram de saturar o app**: respondem em sub-segundo sob carga (0,3–0,9 s vs
~7 s) e só esbarram no rate limit da borda. Erros = `429` da borda (confirmado por rajada: 70 req
→ 53×200 / 17×429), não 5xx — o app não caiu. Redução de queries de version ao DB medida no
profiling: **−99,9%** (129.494 → 132 em 90 s).

**Consequência operacional:** o `limit_req` do nginx (30 req/s/IP) agora é a trava de **todos** os
fluxos. Antes, aumentá-lo não adiantaria (o app saturava a ~21 req/s, abaixo dos 30); **agora sim**
— o app aguenta bem além de 30 req/s por requisição individual, então subir o limite da borda (ou
escalar horizontalmente) destravaria mais capacidade real.

**Regressão futura:** comparar contra o pós-otimização (7.2). Sinal de alarme = p95 das respostas
`200` dos fluxos pesados (`titulos`/`historico`) subir acima de ~1–1,5 s em carga equivalente, ou
o app voltar a saturar (p95 na casa dos segundos) **antes** de bater no teto da borda.

**Limitação de método:** o "joelho" exato (em qual nº de VUs a latência estoura) exigiria a série
temporal do k6 em tempo real, que **não** ficou disponível ao vivo contra prod — não por faltar uma
flag num Prometheus de prod (essa topologia não existe mais desde a 77.4: hoje não há nenhum
Prometheus always-on em produção, e mandar as métricas `k6_*` para o Grafana Cloud é proibido por
cardinalidade alta e transitória, ver `infra/alloy/README.md:61-63`), mas porque medir o joelho ao
vivo exige apontar o k6 para um Prometheus que receba remote-write, e não há um assim em produção
por desenho. Para o joelho granular, rodar com `--local` (overlay `docker-compose.load.yml`, sobe o
Prometheus efêmero só sob `--profile load`) e consultar a UI do próprio Prometheus em
`http://localhost:9090/prometheus/` durante o run — não há mais dashboard Grafana automático (ver
seção 5).

O que **foi** validado estruturalmente:
- `k6 inspect` processa os 6 scripts com sucesso quando as variáveis obrigatórias são passadas
  (`API_URL`/`WEB_URL`, `API_KEY`/`CLIENT_API_KEY`) e falha com mensagem clara quando faltam
  — confirma que o guard de alvo obrigatório funciona e que os scenarios (`smoke`/`ramp` com
  `exec` distinto e `startTime`) estão bem formados.
- O JSON do dashboard (`infra/grafana/dashboards/load-test.json`) é JSON válido.
- O overlay `docker-compose.load.yml` combinado com `docker-compose.yml` passa em
  `docker compose config` (incluindo o guard `${K6_PROMETHEUS_RW_SERVER_URL:?...}` do serviço
  `k6`, que falha explicitamente sem a variável).

### 7.3 Teto real de capacidade (2026-08-09) — API e site

Medições contra produção para achar o limite real de cada frente.

**API — teto de throughput** (para achar o limite do app, o rate limit do nginx foi elevado
temporariamente de 30 para 100 req/s por IP e revertido ao final; ramp de `GET /v1/titulos` a
partir de 1 IP):

| Carga | Comportamento (VPS 1 vCPU / 2 GB) |
|---|---|
| ~30 req/s (limite atual por IP) | tranquilo — p95 sub-segundo, CPU com folga |
| ~50–70 req/s | **joelho** — CPU a 100%, p95 começa a subir (~0,5–1 s) |
| ~90–95 req/s | **teto sem erro** — p95 ~1,3 s, **CPU saturada (load ~2,9 num 1 vCPU)**, ainda 0 erro (0 5xx/429) |
| > 95 req/s | não forçado (latência já alta; exige mais CPU) |

- **Gargalo = CPU (1 vCPU).** Memória não foi o limite. O app não caiu.
- **Consequência:** subir o `limit_req` do nginx só ajuda até ~90 req/s; além disso o app satura
  a CPU — para mais capacidade, escalar vCPU (vertical) ou horizontal.

**Quantos usuários diferentes cabem juntos.** O rate limit de **30 r/s é por IP** (anti-abuso):
cada usuário vem de um IP diferente e tem seu próprio balde, então usuários distintos **não**
disputam esses 30 r/s entre si — um usuário só encostaria nesse limite se sozinho fizesse >30
req/s. Logo, o número de usuários **não** é limitado pelos 30 r/s, e sim pelo **teto global do
app (~90 req/s, CPU)**. O número de usuários é `req/s suportado ÷ requisições por segundo de cada
usuário`:

| Ritmo de cada usuário | No teto do app (**90 req/s**) | (se 30 r/s fosse global¹) |
|---|---|---|
| 1 req/s (intenso) | ~90 usuários | ~30 |
| 1 req a cada 3 s | ~270 | ~90 |
| 1 req a cada 5 s (navegação) | ~450 | ~150 |
| 1 req a cada 10 s (leitura) | ~900 | ~300 |
| 1 req a cada 30 s | ~2.700 | ~900 |

¹ coluna só de referência — os 30 r/s **não** são teto global, são por IP.

**Degradação conforme a carga sobe até ~90 req/s** (ritmo de navegação, ~0,2 req/s por usuário):

| Carga total | Usuários simultâneos (~) | p95 | Estado da VPS (1 vCPU) |
|---|---|---|---|
| ~30 req/s | ~150 | ~0,3 s | folgado |
| ~50 req/s | ~250 | ~0,5 s | CPU ~100%, ainda ok |
| ~70 req/s | ~350 | ~1 s | **joelho** (fila de CPU começa) |
| ~90–95 req/s | ~450 | ~1,3 s | saturado (load ~2,9), **0 erro** |
| > 95 req/s | > ~475 | > 2 s / risco | não recomendado sem mais vCPU |

> A tabela de usuários é aritmética. Na tabela de degradação, o teto (~90 r/s) e os extremos
> (~30 e ~90 r/s) são **medidos**; os pontos intermediários (50/70 r/s) são **interpolados** do
> p95 agregado do ramp + do loadavg da VPS (o teste exportou o p95 do ramp inteiro, não por faixa).

**Site — circuitos SignalR concorrentes** (`site/circuits.js`, ramp monitorado, abort por memória):

- **≥ 343 circuitos simultâneos** sustentados com folga; **~0,2–0,3 MiB de RAM por circuito** na
  VPS → memória **não** é o gargalo (o teto de RAM seria da ordem de milhares).
- O limite prático do site é a **taxa de novas conexões pela borda nginx (`web: 10 r/s`)**: sob
  rajada de acessos/reconexões, a home retorna `429`. Circuitos já conectados e ociosos custam
  quase nada.
- Ou seja: o site sustenta **centenas de usuários navegando ao mesmo tempo**; o cuidado é uma
  rajada de muitos acessos novos no mesmo segundo (> 10/s).

### 7.4 Re-medição com os limites da 74.3 aplicados (2026-08-13) — correção de método

Contexto: a tarefa 74.3 aplicou limites de recursos (cgroup v2 — `cpu_shares`/`cpus`, memória)
aos 8 containers da stack. Esta seção **re-mede** a capacidade da API contra produção com esses
limites em vigor, e corrige um vício de método que inflava os números da §7.3.

**O vício de método.** Medindo do laptop via `https`+nginx, ~99% da latência observada é **rede
do medidor**, não aplicação: de dentro da VPS, `curl http://localhost:5000/v1/titulos` responde
em **4–12 ms**; do laptop, o mesmo endpoint dá **~1000 ms total / ~650 ms de TTFB**. Decomposição
do 1 s: TCP connect 190 ms, TLS +200 ms, TTFB 650 ms, e mais ~380 ms transferindo os
**73.153 bytes** do corpo. A §7.3 reportou p95 de 0,3–1,3 s como se fosse latência de
aplicação — não era.

Por isso a 74.5 mudou três coisas no método:

1. A métrica que decide teto e joelho passa a ser **`http_req_waiting`** (TTFB menos
   conexão/TLS = tempo de servidor + 1 RTT). Linha de base ociosa medida: ~195–200 ms, o que é
   essencialmente a RTT de 190 ms. `http_req_duration` continua reportado, mas rotulado como
   experiência ponta a ponta — **não** decide teto.
2. **Dois fluxos**: `full` = `GET /v1/titulos` sem `If-None-Match` → 200 com 73 KB (tráfego
   realista, satura banda); `etag` = o mesmo request com `If-None-Match` → **304 com corpo
   vazio** (mesmo caminho de código, zero bytes — isola CPU/app da banda). O ETag é lido em
   `setup()`, nunca fixo.
3. **Degraus discretos** (`constant-arrival-rate`, 60 s cada, ~10 s de dreno entre eles), com
   p95 **por degrau**. Corrige a limitação de método registrada na §7.2, que só tinha o p95 do
   ramp inteiro e por isso **interpolava** o joelho.

A borda foi aberta temporariamente (zona `api` em 200 e depois 300 r/s), com watchdog de
reversão armado antes do run, e revertida ao final com prova em `nginx -T`.

**Fluxo `etag` (304, zero bytes) — é o que decide teto e joelho.** Coluna "servidor" = p95
menos a RTT medida de 190 ms. Os degraus 25–50 vêm de uma execução e 60–150 de outra:

| ofertado | atendido | p50 | p90 | p95 | p99 | servidor | erros |
|---|---|---|---|---|---|---|---|
| 25 | 25,00 | 196 | 212 | 222 | 255 | ~32 ms | 0 |
| 30 | 30,02 | 199 | 220 | 249 | 1630 | ~58 ms | 0 |
| 40 | 40,00 | 198 | 239 | 370 | 1046 | ~180 ms | 0 |
| 50 | 50,02 | 200 | 362 | 749 | 1173 | ~559 ms | 0 |
| 60 | 60,00 | 195 | 410 | 721 | 2317 | ~531 ms | 0 |
| 80 | 80,02 | 196 | 638 | 1008 | 3706 | ~818 ms | 0 |
| 100 | 100,02 | 265 | 1154 | 1529 | 3758 | ~1338 ms | 0 |
| 120 | **119,48** | 313 | 1186 | 3051 | 14878 | ~2861 ms | 0 |
| 150 | **138,82** | 580 | 1727 | 3476 | 15566 | ~3286 ms | **135** |

(Latências em ms.)

- **Teto ≈ 120 req/s** — é onde a vazão atendida para de acompanhar a ofertada (119,48 de 120;
  depois 138,82 de 150, já com 135 erros).
- **Capacidade limpa = 100 req/s** — atendido igual ao ofertado, zero erro.
- **Joelho ≈ 100 req/s** — é onde o **p50 sai do platô**: fica plano em 195–200 ms de 25 até
  80 req/s (~10 ms de servidor na mediana, o mesmo valor medido em repouso dentro da VPS) e só
  então sobe para 265, 313, 580. Antes disso o que cresce é só a **cauda**.
- O ruído nos degraus baixos (p95 de 418 ms a 5 req/s, 445 ms a 15 req/s, não incluídos na
  tabela) é jitter do link do medidor com amostra pequena (300–900 requisições por degrau), não
  servidor.
- **Variância entre execuções é real**: o degrau de 60 req/s deu p95 de 1105 ms numa execução e
  721 ms na outra (a tabela reporta o valor da execução usada para compor a série 60–150).

**Fluxo `full` (200 com 73 KB).** Degraus 5, 10, 15, 20, 25, 30, 40, 50, 60, 80 req/s: **a
vazão atendida foi igual à ofertada em todos**, com **zero 429, zero 5xx e zero erro**. A
80 req/s isso são **48,8 Mbit/s**. O teto deste fluxo **não** foi encontrado — a medição parou
em 80 req/s. O sinal importante: **`http_req_receiving` p95 ficou plano em ~420–455 ms em
TODOS os degraus**, de 5 a 80 req/s. Latência de download que não varia com a carga é o link do
medidor, não o servidor — é a prova direta do vício de método descrito acima.

**Lado da VPS durante o run** (amostragem de cgroup v2 a cada 5 s): loadavg de 1 min foi de
**0,32 a 8,27** num único vCPU, e **zero `oom_kill`** nos 8 containers. Pico de memória e
throttling do CFS. As colunas de throttling são **delta dentro da janela do run** (último menos
primeiro), não o valor lido do cgroup: `cpu.stat` acumula desde o boot do container, e reportar o
absoluto superestimaria tudo em uma ordem de grandeza — a coluna "desde o boot" está ao lado só
para deixar a diferença visível. Nota: as linhas `grafana`/`loki`/`prometheus`/`promtail`/
`node-exporter` abaixo são registro datado da 74.5 — essa stack local saiu do ar na 77.4, hoje
substituída por um único container `alloy` que só faz scrape/tail e remote-write/loki-write para o
Grafana Cloud (ver `infra/alloy/README.md`). Números preservados como estavam:

| container | pico | % do teto | eventos (delta) | throttled no run | (desde o boot) |
|---|---|---|---|---|---|
| app | 190 MiB / 256 | 74% | 26 | **0,1 s** | 2,0 s |
| db | 89 MiB / 128 | 69% | 100 | 1,9 s | 8,5 s |
| grafana | 110 MiB / 160 | 69% | 9 | 0,5 s | 89,6 s |
| loki | 131 MiB / 132 | **99%** | 27 | 1,1 s | 52,7 s |
| prometheus | 45 MiB / 48 | **95%** | 4 | 0,2 s | 52,7 s |
| promtail | 18 MiB / 36 | 52% | 1110 | **62,3 s** | 704,2 s |
| web | 81 MiB / 160 | 50% | 0 | 0,0 s | 5,6 s |
| node-exporter | 9 MiB / 16 | 61% | 6 | 0,3 s | 80,4 s |

**Leitura que importa:** o teto **não** é a cota de CPU do `app` — ele levou **0,1 s** de
throttling nos **6m37s** amostrados desta execução (degraus 60–150), contra 62,3 s do `promtail`
no mesmo intervalo. O gargalo é disputa pelo único vCPU
físico (loadavg 8,27), e o
desenho da 74.3 (`cpu_shares` como peso + `cpus` como teto folgado) funcionou como projetado:
sob pressão o `app` ganha o ciclo e a observabilidade cede. `loki` a 99% e `prometheus` a 95%
são **achados de orçamento sob carga** — não houve OOM, mas é apertado.

**Comparação com a §7.3, com a ressalva.** A §7.3 (antes dos limites) reportou teto ~90–95 req/s
com loadavg ~2,9. **Não é comparação direta**: a §7.3 mediu com corpo completo. No fluxo
equivalente (`full`) esta medição chegou a 80 req/s sem um único erro sem procurar o teto,
limitada pela banda do medidor. O que se pode afirmar: **os limites da 74.3 não tornaram o app
mais lento**.

**Tabelas de usuários simultâneos — recalculadas com o número medido (aritmética, não
medição).** A §7.3 dizia ~450 usuários no teto (~90 req/s antigo). Com **joelho 100 req/s** e
**teto 120 req/s**:

| Ritmo de cada usuário | No joelho (100 req/s) | No teto (120 req/s) |
|---|---|---|
| 1 req/s (intenso) | 100 | 120 |
| 1 req a cada 3 s | 300 | 360 |
| 1 req a cada 5 s (navegação) | 500 | 600 |
| 1 req a cada 10 s (leitura) | 1.000 | 1.200 |
| 1 req a cada 30 s | 3.000 | 3.600 |

Como já explicava a §7.3 e continua valendo: os 30 r/s do `limit_req` são **por IP**, não teto
global — usuários distintos não disputam esse balde entre si.

**`limit_req` da borda — re-derivado e mantido em `rate=30r/s burst=50`.** Decisão do dono; o
`infra/nginx/tesouro-direto.conf` **não muda** nesta fase, o entregável é a derivação:

- O limitador é anti-abuso **por IP**. Com joelho de 100 req/s, 30 r/s por IP significa que são
  necessários **~3,3 IPs abusivos simultâneos** para levar a máquina ao joelho e ~4 para o teto.
- **Nenhum 429 é de usuário orgânico.** No log do nginx de 2026-08-13 há **753.454** respostas
  `429`, e a decomposição por IP fecha em dois:
  **753.436 do IP do medidor** — as horas 03h e 04h são o run que ficou preso (ver Limitações), e
  a hora 10h sozinha tem **752.194**, que é a tentativa de medir circuitos com a zona `web` ainda
  em 10 r/s (o mesmo incidente de 751.695 × `429` citado no bloco de circuitos);
  e **18 de um segundo IP**, um scanner sondando `.aws/credentials`, `.env` e `/RDWeb/`.
  Esses 18 são o limitador **fazendo exatamente o trabalho dele**.
  **Cuidado ao citar isto como prova:** descontado o medidor, o volume orgânico na janela é de
  poucas dezenas de requisições. É evidência de que o limitador não está atrapalhando usuário,
  **não** uma prova de alto volume. Para essa prova seria preciso uma janela sem teste de carga.
- **A premissa registrada de que os 30 r/s estariam "apertados demais" está refutada.** Ela
  vinha de "69,6% das requisições batem no limitador"; o log de 2026-08-12 mostra 63,35% de 429
  em 165 mil requisições, e 12/08 foi dia de teste de carga — o número media o próprio medidor.
- O risco que o plano previa — o app cair para 15–25 req/s e os 30 r/s deixarem de proteger o
  backend — **não se materializou**: o app faz 120 req/s. A estimativa do plano supunha `app`
  com 0,15 vCPU rígido; a config real é `cpu_shares: 1024` com teto folgado `cpus: "0.70"`.

**Limitações desta medição.**

- Medidor fica fora da VPS, atrás de ~190 ms de RTT; por isso `http_req_waiting` e não
  `http_req_duration`, mas a RTT ainda está embutida no número.
- 60 s por degrau dá 300–900 amostras nos degraus baixos: p95/p99 são ruidosos ali.
- O teto do fluxo `full` não foi encontrado (banda do medidor).
- Uma execução foi perdida: o laptop do medidor entrou em *idle sleep* no meio, o k6 ficou 1h45
  com conexões penduradas e, durante a pausa, o watchdog de reversão fechou a borda no horário
  programado — as retentativas então bateram em `429`. Esse ponto foi **descartado** e
  re-medido sob `caffeinate`. Vale como aviso de operação: **rodar carga longa sempre sob
  `caffeinate`**.

**Circuitos Blazor sob o teto de memória da 74.3.** A §7.3 afirmava "≥343 circuitos simultâneos,
~0,2–0,3 MiB por circuito, memória não é o gargalo". Dois defeitos nessa medição: (a) foi feita
**sem** teto de memória no container — hoje o `web` tem `memory: 160M`; (b) o run de 2026-08-09
foi **abortado pelo guard do próprio medidor** (`MemAvailable` do **host** abaixo de 220 MiB), então
343 é onde o medidor parou, não onde o servidor cedeu.

**Método corrigido.** O guard passou a ler o **cgroup v2 do container** `tesouro-direto-web` (não
o host), distinguindo três pontos: teto de RAM (`memory.current` ≥ 95% de `memory.max`), teto
prático (swap/`pgmajfault` sustentados + latência de handshake subindo) e teto absoluto
(`oom_kill`/`RestartCount`). A zona `web` do nginx foi **aberta para 100 r/s** durante o run — com
ela nos 10 r/s de produção o teste mede o nginx recusando e não o container (uma tentativa
anterior produziu 751.695 respostas `429` contra 192 handshakes bem-sucedidos). Cada VU segura um
circuito; em caso de falha há backoff exponencial (1s→30s) e o VU desiste após 5 falhas seguidas,
para que uma recusa transitória não vire tempestade de retentativas.

**Resultado por patamar** (VUs simultâneos, cada um segurando um circuito):

| VUs | handshakes ok | falhas do container | 429 da borda |
|---|---|---|---|
| 50 | 4 | 0 | 0 |
| 100 | 24 | 0 | 0 |
| 150 | 58 | 0 | 0 |
| 200 | 106 | 0 | 0 |
| 250 | 160 | 0 | 0 |
| 300 | 206 | 0 | 0 |
| 350 | 256 | 0 | 0 |
| **400** | 279 | **307** | 0 |
| **500** | 203 | **401** | 0 |

(handshakes ok = handshakes concluídos durante a janela de hold daquele patamar, não circuitos
simultâneos.) Total do run: 2.177 ok, 1.398 falhas de container, **zero 429**, 119 VUs
desistiram. Latência de handshake ~2,0–2,2 s de 50 a 350 VUs, subindo para ~2,5 s em 400.

**Memória: não é o gargalo, e agora isso está medido sob o teto.** Pico do `web`: **146,6 MiB de
160 (91%)** a 495 VUs — os 95% do teto de RAM **nunca** foram atingidos. Swap total **1,08 MiB**,
apenas **5 `pgmajfault`** no run inteiro (242→247), zero `oom_kill`, zero restart. Sem paginação,
sem thrashing. Progressão do consumo: 105,7 MiB em repouso → 68% a 50 circuitos → 75% a 150 → 82%
de 300 a 350 (platô, o GC segurando) → 91% no pico.

**O gargalo real é a borda, não o container — e explica o número da §7.3.** Nenhum limite da 74.3
foi atingido: memória parou em 91%, `pids.events` do cgroup mostra `max 0` (o limite de 128 nunca
foi tocado; 14 em repouso), e o throttling de CPU do `web` no run somou ~2,1 s. A causa está no
nginx:

    worker_processes auto;    →  nproc = 1
    worker_connections 768;
    error.log durante o run: "768 worker_connections are not enough"

Um único worker com 768 conexões. **Cada circuito Blazor proxiado consome duas conexões**
(cliente→nginx e nginx→container), então o teto aritmético é **768 ÷ 2 = 384 circuitos** — e as
falhas começaram em 400. O processo nginx também roda com `Max open files` soft de **1024**, que
morderia logo em seguida. **Os "≥343" da §7.3 eram esta mesma parede**, atribuída na época ao
guard de memória do host: dois runs com limites de container
completamente diferentes pararam no mesmo número porque o limite nunca esteve no container.

**O que "350 circuitos" significa aqui — leia antes de citar o número.** Os circuitos **não**
ficaram parados: cada VU segurou o seu por **75 s**, fechou e reabriu, ~7 vezes ao longo do run
(3.548 iterações completas para ~500 VUs). Foi um defeito do medidor, não escolha: o script de
cenário define um `HOLD_MS_CIRCUITO` de 15 min como constante local, mas quem lê o valor é
`tests/load/site/circuits.js` via `__ENV.HOLD_MS`, e o runner nunca exporta essa variável — então
valeu o default de 75 s. Portanto o que foi medido são **~350 circuitos concorrentes sob
reciclagem contínua** (~4–5 conexões novas/s no topo), não 350 circuitos ociosos segurados. Isso
torna o número **conservador** para o caso de uso real (usuário que abre a página e fica), porque
inclui o custo de reconexão o tempo todo — mas quem quiser o teto de circuitos ociosos precisa
re-medir exportando `HOLD_MS`. É também parte da explicação das falhas a partir de 400 VUs: a
reciclagem soma pressão de conexões novas sobre a mesma zona de `worker_connections`.

**Consequência prática.** O site sustenta **~350 circuitos simultâneos** hoje. Elevar esse número
é mudança de configuração da **borda** (`worker_connections` e o `nofile` do processo nginx), não
de recurso de container — subir o `memory` do `web` não compraria um circuito sequer. Essa mudança
de nginx **não** foi feita na 74.5 (fica como follow-up). **Armadilha para quem for fazê-la:** o
`worker_connections` em vigor **não está no repositório**. O `infra/nginx/nginx.conf` versionado
diz `1024`, mas quem vale na VPS é `/etc/nginx/nginx.conf` — arquivo comum, de 2023, com o default
`768`. Só o `tesouro-direto.conf` é symlink do repo; o `nginx.conf` principal não é, então `git
pull` não o atualiza e uma VPS nova não herda a mudança. Mesma fragilidade já registrada para o
timer do dead-man. O `limit_req` da zona `web` continua em
10 r/s de produção, restringindo a **taxa de novas conexões**, que é coisa diferente do número de
circuitos simultâneos.

**Limitações.** O contador de circuitos do medidor é cumulativo de aberturas, então a
concorrência real é inferida do nº de VUs — e, pela reciclagem de 75 s descrita acima, ela oscila
em torno desse número em vez de ser exatamente ele. O marco de "teto prático" do medidor disparou um
falso positivo a 50 VUs porque a condição implementada testa `swap > 0` em vez de `swap`
crescendo — o swap estava em 480 KiB residuais desde antes do run e nunca cresceu de forma
relevante.

## 8. Não entra na pipeline

Este teste de carga **não roda em CI/CD** — é uma ferramenta sob demanda, executada
manualmente por quem for investigar capacidade/regressão de performance antes de um deploy
relevante ou ao investigar um incidente. Não há gate de pipeline associado a ele.
