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

Durante o run (streaming via remote-write, não só ao final): dashboard **"Load Test (k6)"**
(uid `load-test-k6`), auto-provisionado —

```
http://localhost:3000/grafana/d/load-test-k6
```

Painéis: VUs, requisições/s, latência p95/p99, taxa de erro HTTP, taxa de 304 (ETag sob carga,
só aparece rodando `titulos.js`), circuitos SignalR concorrentes (Blazor), e CPU/memória do
host via node-exporter.

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

Capturado em **2026-08-08** contra **produção** (VPS, atrás do nginx+TLS), ramp de 0→200 VUs
a partir de **1 IP** (o número de origem importa — ver a nota sobre a borda abaixo). Números do
resumo do `k6` (`--summary-export`); o `site/circuits.js` não foi executado contra prod (segurar
circuitos Blazor exige a validação do handshake contra o servidor real — ver seção 6).

| Fluxo | Throughput obs. | p95 | Taxa de erro | Taxa de 304 | Teto atingido |
|---|---|---|---|---|---|
| `api/titulos.js` (GET + ETag) | ~23 req/s | 6,9 s | 0% | 100% | **Capacidade da VPS** (satura em latência) |
| `api/historico.js` (GET paginado) | ~21 req/s | 7,2 s | ~0,01% | N/A | **Capacidade da VPS** (satura em latência) |
| `api/preco-atual.js` (GET) | ~132 req/s¹ | 2,2 s | 82% (429 da borda) | N/A | **Borda nginx** (~30 req/s por IP) |
| `api/simulador.js` (POST) | ~315 req/s¹ | 223 ms | 95% (429 da borda) | N/A | **Borda nginx** (~30 req/s por IP) |
| `site/circuits.js` | não executado contra prod | N/A (mede circuitos concorrentes, não latência) | N/A | N/A | — |

¹ Throughput inflado pelas respostas `429` (que são rápidas): o número alto é a taxa de tentativas,
não de sucesso. O sucesso real (`status 200`) foi ~17% no `preco-atual` e ~5% no `simulador`.

**Os dois tetos distintos da configuração (a conclusão principal):**

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

**Regressão futura:** comparar contra estes tetos. Sinal de alarme = throughput de sucesso cair
abaixo de ~20 req/s nos fluxos pesados, ou p95 dos fluxos pesados subir acima de ~7 s em carga
equivalente.

**Limitação de método:** o "joelho" exato (em qual nº de VUs a latência estoura) exigiria a série
temporal no Grafana, que **não** ficou disponível ao vivo em prod — o Prometheus de prod está sem
`--web.enable-remote-write-receiver`, e o `--web.external-url=/prometheus/` impede o receiver mesmo
se ligado (Prometheus 3.13.2). Para o joelho granular, rodar em um ambiente com o receiver
habilitado (overlay `docker-compose.load.yml`) e ler o dashboard `load-test-k6` durante o run.

O que **foi** validado estruturalmente:
- `k6 inspect` processa os 6 scripts com sucesso quando as variáveis obrigatórias são passadas
  (`API_URL`/`WEB_URL`, `API_KEY`/`CLIENT_API_KEY`) e falha com mensagem clara quando faltam
  — confirma que o guard de alvo obrigatório funciona e que os scenarios (`smoke`/`ramp` com
  `exec` distinto e `startTime`) estão bem formados.
- O JSON do dashboard (`infra/grafana/dashboards/load-test.json`) é JSON válido.
- O overlay `docker-compose.load.yml` combinado com `docker-compose.yml` passa em
  `docker compose config` (incluindo o guard `${K6_PROMETHEUS_RW_SERVER_URL:?...}` do serviço
  `k6`, que falha explicitamente sem a variável).

## 8. Não entra na pipeline

Este teste de carga **não roda em CI/CD** — é uma ferramenta sob demanda, executada
manualmente por quem for investigar capacidade/regressão de performance antes de um deploy
relevante ou ao investigar um incidente. Não há gate de pipeline associado a ele.
