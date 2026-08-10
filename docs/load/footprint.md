# Footprint de recursos dos containers (tarefa 74.1)

Este documento é irmão de [`docs/load/README.md`](./README.md): aquele mede capacidade
(req/s, latência, circuitos concorrentes); este mede **quanto cada container realmente
consome de RAM e CPU**, para alimentar o orçamento 25% app / 25% infra / 50% livre da
tarefa 74 (`docs/PLANO.md`, seção `### 74.`, parágrafo **74.1**). Medido numa VPS de
**1 vCPU / 1967 MB** (`free -m` na VPS, confirmado ao escrever este documento).

## 1. O que este documento é e como foi medido

A ferramenta é [`tests/load/profiling/run-footprint.sh`](../../tests/load/profiling/run-footprint.sh),
nova (não é extensão do `run-profile.sh`, que é destrutivo — zera `pg_stat_statements`).
Ela lê **cgroup v2 direto** em `/sys/fs/cgroup` — `memory.current`, `memory.peak`,
`memory.stat` (`anon`, `file`, `shmem`, `kernel`, `slab`), `memory.events` (`oom_kill`),
`cpu.stat` (`usage_usec`, `nr_periods`, `nr_throttled`, `throttled_usec`) e `pids.current`
— e só usa `docker stats` por cima, para `NetIO`/`BlockIO` (não há equivalente simples em
cgroup v2 para essas duas). É só leitura: nunca escreve, reinicia ou faz prune de nada,
pensada para rodar em produção com outras aplicações de pé.

**Por que `docker stats` sozinho engana:** o `MemUsage` que ele mostra é
`memory.current − inactive_file` — ainda inclui page cache (a parte do `file` que não é
`inactive_file`), então superestima o que realmente importa para decidir um OOM-kill, que
é `anon + kernel` (+ `shmem`, que o `docker stats` não separa). Dimensionar um limite de
container pelo `MemUsage` do `docker stats` reserva memória para page cache que o kernel
descarta sob pressão antes de qualquer coisa ruim acontecer.

**Por que o relatório reporta p50/p95/máximo, e nunca média:** uma média esconde picos —
e é o pico, não o típico, que decide se um container é morto por OOM ou throttled pelo
CFS. Convenção idêntica à de `docs/load/README.md` (latências reportadas em p95/p99, nunca
média).

Cada janela também mede **CPU e throttling** (`cpu.stat`), mas hoje nenhum container tem
`cpus:` no `docker-compose.yml` (pré-74.3) — por isso a coluna "throttling" aparece como
`n/d (sem período de CPU registrado — sem limite de cpus hoje)` em toda a tabela: sem
quota, `nr_periods` fica em 0 e não há o que dividir. Essa coluna começa a preencher só
depois da 74.3.

## 2. A coluna que importa: `não-descartável`

`não-descartável = anon + shmem + kernel`. O raciocínio:

- **Page cache (`file − shmem`) é descartável** — o kernel o libera sob pressão de memória
  sem custo funcional (é só um cache de leitura de disco). Dimensionar por `memory.current`
  (que inclui esse cache inteiro) **superestima** o limite necessário.
- **`anon` sozinho subestima**, porque ignora `shmem` — memória compartilhada anônima
  (tmpfs, segmentos `/dev/shm`) que **não** é descartável do mesmo jeito que page cache: só
  sai do container via swap, nunca é liberada de graça sob pressão.
- **`slab` não entra na soma.** No cgroup v2, `slab` já está **dentro** de `kernel` —
  somar os dois contaria a mesma memória duas vezes. Verificado nesta VPS, no container
  `app` (comentário em `tests/load/profiling/run-footprint.sh`, função
  `stats_nao_descartavel`): `kernel = 3002368` bytes contra
  `slab + kernel_stack + pagetables + percpu + sock + vmalloc = 2988596` bytes — a
  diferença de ~13 KB é só alocações kernel menores não detalhadas por essas categorias.
  `slab` continua na tabela como coluna própria (é útil vê-la isolada), só não entra na
  soma de "não-descartável".

**Exemplo concreto**, `app` ocioso (`idle-report.md`): `memory.current` fica em
**122,0–123,7 MB** (p50–máximo) — pareceria que o app precisa de ~124 MB de limite. Mas
`anon` sozinho é só **55,0–57,1 MB** (subestima, ignora o resto) e `shmem` é **57,8–58,1 MB**
(quase do tamanho do `anon` — é maioria de "não-descartável", não ruído). A soma
`anon+shmem+kernel` dá **115,8–117,9 MB**: abaixo do `memory.current` (que incluía ~6 MB de
page cache descartável) e bem acima do `anon` isolado.

## 3. Uma tabela por janela

Três janelas coletadas até aqui (`cold` e `deploy` ficam para a seção 7 — pendentes).
Todas rodaram com [`run-footprint.sh`](../../tests/load/profiling/run-footprint.sh) na VPS
de produção, leitura pura. Os CSVs brutos **ficam só na VPS** (nunca sobem para o repo —
o deploy faz `git reset --hard` e os apagaria).

### 3.1 `idle` — 60 min @ 60s, madrugada, sem carga

CSV bruto: `/var/tmp/footprint/idle-20260810-021047.csv`. Amostras: 60 ciclos, 480 linhas
de container. Janela real: 3600s (02:10:49 → 03:09:49 UTC).

| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | CPU% (p50/p95/máx) |
|---|---|---|---|---|---|---|---|---|---|---|
| tesouro-direto-app | 1 (não) | 122.0/123.4/123.7 | 126.2 | 55.0/56.1/57.1 | 64.0/64.4/64.4 | 57.8/58.1/58.1 | 2.9/2.9/3.0 | 1.8/1.8/1.9 | 115.8/117.1/117.9 | 1.3/1.4/1.6 |
| tesouro-direto-db | 1 (não) | 55.3/56.7/56.7 | 269.0 | 2.0/3.8/3.8 | 48.8/48.8/49.3 | 34.0/34.0/34.5 | 2.1/2.3/2.3 | 1.2/1.2/1.2 | 38.1/39.8/39.8 | 0.9/0.9/1.0 |
| tesouro-direto-grafana | 1 (não) | 111.7/113.5/113.5 | 134.9 | 101.8/103.2/103.2 | 6.4/6.4/6.4 | 0.0/0.0/0.0 | 4.0/4.0/4.0 | 2.8/2.8/2.8 | 105.8/107.2/107.2 | 0.6/0.7/0.7 |
| tesouro-direto-loki | 1 (não) | 113.7/136.1/146.4 | 159.3 | 70.1/92.5/102.9 | 42.1/42.6/42.7 | 0.0/0.0/0.0 | 1.5/1.5/1.5 | 0.8/0.8/0.8 | 71.6/93.9/104.4 | 0.6/0.7/0.7 |
| tesouro-direto-node-exporter | 1 (não) | 17.9/18.1/18.1 | 18.5 | 8.1/8.3/8.4 | 9.0/9.1/9.1 | 0.0/0.0/0.0 | 0.7/0.7/0.7 | 0.5/0.5/0.5 | 8.8/9.0/9.1 | 0.1/0.2/0.2 |
| tesouro-direto-prometheus | 1 (não) | 42.8/49.7/52.7 | 53.0 | 37.1/40.6/43.8 | 4.6/7.8/7.9 | 0.0/0.0/0.0 | 1.1/1.2/1.2 | 0.4/0.5/0.5 | 38.1/41.8/45.0 | 0.1/0.1/0.2 |
| tesouro-direto-promtail | 1 (não) | 70.2/70.3/70.3 | 70.6 | 15.6/15.7/15.7 | 53.5/53.6/53.6 | 0.0/0.0/0.0 | 1.0/1.0/1.0 | 0.5/0.5/0.5 | 16.6/16.7/16.7 | 0.3/0.4/0.4 |
| tesouro-direto-web | 1 (não) | 62.3/66.6/66.6 | 66.9 | 26.8/29.6/29.6 | 33.4/34.9/35.0 | 30.7/32.2/32.2 | 2.1/2.1/2.1 | 1.2/1.2/1.2 | 59.6/63.8/63.9 | 0.2/0.4/0.8 |

Host (`idle`): mem. usada 909,0/936,0/941,0 MB (p50/p95/máx); disponível 1058,0/1071,0/1079,0;
swap 36,0/37,0/37,0; load1 0,1/0,3/0,8; disco 26,0%.

### 3.2 `load-api` — 15 min @ 5s, `titulos.js` (0→200 VUs) × 3, borda `zone=api rate=100r/s`

CSV bruto: `/var/tmp/footprint/load-20260810-093743.csv`. Amostras: 180 ciclos, 1440 linhas
de container. Janela real: 896s (09:37:46 → 09:52:42 UTC). Rótulo registrado no CSV:
"k6 titulos ramp x3 @ nginx 100r/s".

| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | CPU% (p50/p95/máx) |
|---|---|---|---|---|---|---|---|---|---|---|
| tesouro-direto-app | 1 (não) | 189.2/216.0/241.3 | 242.5 | 98.8/124.9/150.3 | 86.1/86.8/86.9 | 64.7/64.8/64.9 | 4.2/4.8/5.0 | 3.0/3.3/3.4 | 167.8/194.4/219.7 | 21.8/44.7/55.0 |
| tesouro-direto-db | 1 (não) | 235.8/238.2/239.6 | 269.0 | 160.7/162.1/163.9 | 43.5/47.4/47.5 | 28.5/32.5/32.5 | 29.0/29.1/29.4 | 8.1/8.2/8.3 | 218.1/221.0/221.7 | 1.3/31.6/61.5 |
| tesouro-direto-grafana | 1 (não) | 187.2/201.7/202.2 | 203.0 | 131.6/140.2/140.3 | 55.2/57.3/57.9 | 0.0/0.0/0.0 | 4.1/4.2/4.2 | 2.9/2.9/2.9 | 135.7/144.4/144.5 | 1.2/4.5/9.1 |
| tesouro-direto-loki | 1 (não) | 218.5/278.6/312.1 | 321.1 | 115.1/161.0/198.9 | 99.4/121.2/126.5 | 0.0/0.0/0.0 | 2.7/3.4/3.6 | 1.9/2.6/2.7 | 117.8/164.2/201.7 | 6.0/38.5/46.7 |
| tesouro-direto-node-exporter | 1 (não) | 13.7/15.9/15.9 | 19.2 | 8.6/8.9/9.1 | 3.9/6.6/6.6 | 0.0/0.0/0.0 | 0.7/0.9/0.9 | 0.5/0.6/0.6 | 9.3/9.6/9.8 | 0.0/0.6/0.7 |
| tesouro-direto-prometheus | 1 (não) | 69.3/72.3/75.6 | 95.9 | 40.8/43.3/47.7 | 26.9/27.2/28.2 | 0.0/0.0/0.0 | 1.6/1.7/1.7 | 0.9/1.0/1.0 | 42.5/45.0/49.4 | 0.3/1.2/11.7 |
| tesouro-direto-promtail | 1 (não) | 57.5/70.8/72.3 | 72.6 | 35.9/36.0/36.0 | 20.0/40.5/40.8 | 0.0/0.0/0.0 | 1.5/1.5/1.5 | 1.0/1.0/1.0 | 37.4/37.5/37.5 | 0.8/3.6/5.6 |
| tesouro-direto-web | 1 (não) | 84.5/84.5/84.5 | 84.8 | 37.7/37.7/37.7 | 44.7/44.7/44.7 | 35.6/35.6/35.6 | 2.1/2.2/2.2 | 1.2/1.2/1.3 | 75.4/75.4/75.4 | 0.1/0.2/0.3 |

Host (`load-api`): mem. usada 1265,0/1350,0/1380,0 MB; disponível 702,0/1046,0/1054,0;
swap 57,0/58,0/58,0; load1 10,1/29,5/36,3; disco 26,0%.

### 3.3 `load-web2` — 15 min @ 5s, `circuits.js` (até 400 VUs, hold 75s), borda `zone=api` **e** `zone=web` a 100 r/s

CSV bruto: `/var/tmp/footprint/load-20260810-102541.csv`. Amostras: 180 ciclos, 1440 linhas
de container. Janela real: 895s (10:25:43 → 10:40:38 UTC). Rótulo registrado no CSV:
"k6 circuits Blazor ate 400 VUs (nginx web=100r/s)" — **esta é a coleta válida**; a primeira
tentativa (`load-web-report.md`, ver seção 7) foi descartada.

| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | CPU% (p50/p95/máx) |
|---|---|---|---|---|---|---|---|---|---|---|
| tesouro-direto-app | 1 (não) | 174.6/174.7/176.3 | 242.5 | 83.4/83.5/84.9 | 87.3/87.3/87.3 | 64.9/64.9/64.9 | 3.8/4.0/4.1 | 2.6/2.7/2.8 | 152.1/152.3/153.9 | 1.6/5.0/13.1 |
| tesouro-direto-db | 1 (não) | 88.5/99.7/99.7 | 269.0 | 34.6/44.2/44.2 | 43.3/43.3/43.3 | 28.5/28.5/28.5 | 7.7/9.3/9.3 | 2.6/3.0/3.0 | 70.8/82.0/82.0 | 1.0/2.4/20.4 |
| tesouro-direto-grafana | 1 (não) | 202.9/203.7/204.0 | 207.1 | 138.9/139.8/140.0 | 59.7/59.9/59.9 | 0.0/0.0/0.0 | 4.2/4.2/4.2 | 2.9/2.9/2.9 | 143.0/144.0/144.1 | 1.4/4.6/5.3 |
| tesouro-direto-loki | 1 (não) | 217.2/230.7/233.6 | 321.1 | 124.9/132.5/136.6 | 91.9/98.3/98.4 | 0.0/0.0/0.0 | 2.6/2.8/2.8 | 1.7/1.9/1.9 | 127.3/135.3/139.3 | 2.5/6.5/10.0 |
| tesouro-direto-node-exporter | 1 (não) | 13.5/13.7/14.1 | 19.2 | 8.6/8.8/9.2 | 4.0/4.0/4.0 | 0.0/0.0/0.0 | 0.9/0.9/0.9 | 0.6/0.6/0.6 | 9.5/9.7/10.1 | 0.0/0.5/0.6 |
| tesouro-direto-prometheus | 1 (não) | 72.5/75.6/79.7 | 95.9 | 41.6/44.9/48.5 | 29.3/29.5/29.7 | 0.0/0.0/0.0 | 1.7/1.7/1.7 | 0.9/0.9/0.9 | 43.3/46.6/50.2 | 0.3/0.8/1.2 |
| tesouro-direto-promtail | 1 (não) | 41.7/46.0/46.0 | 72.6 | 19.2/23.5/23.5 | 20.9/20.9/20.9 | 0.0/0.0/0.0 | 1.6/1.6/1.6 | 1.0/1.0/1.0 | 20.8/25.1/25.1 | 0.4/0.7/0.8 |
| tesouro-direto-web | 1 (não) | 139.1/141.1/145.2 | 145.8 | 80.3/82.2/85.6 | 56.5/56.5/56.5 | 37.8/37.8/37.8 | 2.4/2.9/3.1 | 1.3/1.8/2.0 | 120.4/122.3/126.4 | 0.8/8.1/22.1 |

Host (`load-web2`): mem. usada 1124,0/1189,0/1207,0 MB; disponível 842,0/899,0/907,0;
swap 58,0/58,0/58,0; load1 0,8/1,7/2,8; disco 26,0%.

Em nenhuma das três janelas houve `OOM kills (cgroup)`, reinício ou `OOMKilled=true` — o
app não caiu em nenhum cenário.

### 3.4 Consolidado — máximo de cada container entre as três janelas

| Container | não-descartável máx entre janelas (MB) | Janela onde ocorreu | mem.peak máx entre janelas (MB) |
|---|---|---|---|
| `app` | 219,7 | load-api | 242,5 |
| `db` | 221,7 | load-api | 269,0 |
| `grafana` | 144,5 | load-api | 207,1 |
| `loki` | 201,7 | load-api | 321,1 |
| `node-exporter` | 10,1 | load-web2 | 19,2 |
| `prometheus` | 50,2 | load-web2 | 95,9 |
| `promtail` | 37,5 | load-api | 72,6 |
| `web` | 126,4 | load-web2 | 145,8 |

`mem.peak` é uma marca d'água **desde o start do container** (não zera entre janelas, só
em recriação/restart-por-política) — por isso pode exceder o pico visto nas três janelas
medidas aqui (caso do `db`, ver seção 5).

## 4. Orçamento: a conta que não fecha

Máquina de **1 vCPU / 1967 MB** ⇒ 25% = **~492 MB** por bloco (1967 × 0,25 = 491,75).
Regra de folga do plano (`docs/PLANO.md`, 74.3): `limite = max(pico × 1,3 ; pico + 64 MB)`,
aplicada aqui sobre o **máximo não-descartável entre as três janelas** (coluna da tabela
3.4). Cada limite é arredondado ao MB mais próximo antes de somar o bloco.

### Bloco APP

| Serviço | pico não-descartável (máx) | pico × 1,3 | pico + 64 | limite (máx dos dois) | limite arredondado |
|---|---|---|---|---|---|
| `app` | 219,7 MB | 285,6 | 283,7 | 285,6 MB | **286 MB** |
| `web` | 126,4 MB | 164,3 | 190,4 | 190,4 MB | **190 MB** |
| **Total APP** | | | | | **476 MB** |

**APP fecha**: 476 de 492 MB — folga de 16 MB.

### Bloco INFRA

| Serviço | pico não-descartável (máx) | pico × 1,3 | pico + 64 | limite (máx dos dois) | limite arredondado |
|---|---|---|---|---|---|
| `db` | 221,7 MB | 288,2 | 285,7 | 288,2 MB | **288 MB** |
| `prometheus` | 50,2 MB | 65,3 | 114,2 | 114,2 MB | **114 MB** |
| `grafana` | 144,5 MB | 187,9 | 208,5 | 208,5 MB | **209 MB**¹ |
| `loki` | 201,7 MB | 262,2 | 265,7 | 265,7 MB | **266 MB** |
| `promtail` | 37,5 MB | 48,8 | 101,5 | 101,5 MB | **102 MB**¹ |
| `node-exporter` | 10,1 MB | 13,1 | 74,1 | 74,1 MB | **74 MB** |
| **Total INFRA** | | | | | **1053 MB** |

¹ arredondamento de 208,5/101,5 para cima (convenção "meio para cima", consistente com o
total de 1053 MB batido abaixo).

**INFRA estoura**: 1053 de 492 MB — **excesso de 561 MB**. `db` (288), `loki` (266) e
`grafana` (209) **sozinhos já somam 763 MB**, mais que o bloco inteiro de 492 MB — nenhuma
combinação de dois desses três cabe no orçamento, e o terceiro nem entrou na conta.

## 5. Achados que mudam decisão

**O `loki` é o problema não óbvio.** Não-descartável dobra sob carga: **104,4 MB** (`idle`,
máximo) → **201,7 MB** (`load-api`, máximo) — `mem.current` chega a **312,1 MB** e
`mem.peak` a **321,1 MB** nessa mesma janela (`load-api-report.md`). A causa é indireta:
rajada de tráfego na API vira rajada de log de acesso do nginx, que o `promtail` encaminha
e o `loki` ingere — carga do `loki` acompanha carga de tráfego, não carga do próprio
serviço. O orçamento provisório do plano (`docs/PLANO.md`, tabela da 74.3) lhe dava **96 MB**
— ele morreria de OOM no primeiro pico de tráfego real, não num cenário hipotético.

**O `db` encosta no teto.** Não-descartável sob carga chega a **221,7 MB**
(`load-api-report.md`, máximo) contra **224 MB** provisionados na tabela original do plano
— folga de 2,3 MB, efetivamente zero. Pior: `mem.peak` = **269 MB**, uma marca d'água que
já aparecia nos números de partida medidos em 2026-08-09 (o container não foi recriado
desde `2026-07-26T15:18:54Z`, confirmado via `docker inspect` na VPS — **~2 semanas** antes
desta coleta) e que **nenhuma das três janelas medidas reproduziu** — o job Quartz diário de
importação roda às `0 0 6 * * ?` (6h **UTC**; `src/TesouroDireto.Infrastructure/DependencyInjection.cs:154`),
e nenhuma das três janelas (02:10–03:09, 09:37–09:52, 10:25–10:40 UTC) cobre esse horário.
Como a composição de memória durante a importação não foi capturada (só a marca d'água),
**dimensionar o `db` contra os 269 MB**, não contra os 221,7 medidos, é a escolha
conservadora e correta até existir uma janela que cubra as 6h UTC.

**O piso de `+64 MB` da regra de folga é ruim para container pequeno.** Para o
`node-exporter`, cujo não-descartável nunca passou de **10,1 MB** nas três janelas (máximo
em `load-web2-report.md`), a regra dá **74,1 MB** de limite — quase 7× o pico medido, todo
ele vindo do piso fixo (`10,1 + 64 = 74,1` vence `10,1 × 1,3 = 13,1`). Somado em
`prometheus` (piso vence: 114,2 MB vs pico de 50,2), `promtail` (piso vence: 101,5 MB vs
pico de 37,5) e o próprio `node-exporter`, o piso fixo injeta **(74,1−13,1) + (114,2−65,3) +
(101,5−48,8) ≈ 61,0 + 48,9 + 52,7 ≈ 162,6 MB** de folga pura num bloco que já estoura em
561 MB — quase um terço do excesso é o piso, não o consumo real. Recomendação: folga
proporcional (só ×1,3, sem o piso de `+64 MB`) para containers com pico abaixo de
~100 MB.

**Cada carga move só o que deveria.** A carga de API (`load-api-report.md`) **não mexeu**
no `web`: não-descartável fica achatado em **75,4 MB** (p50/p95/máx idênticos — o container
mal reagiu). Foi a carga de circuitos (`load-web2-report.md`) que o levou a **126,4 MB** —
alta de 51 MB sobre o mesmo container. As duas janelas medem coisas distintas e nenhuma é
redundante com a outra: prova de que a decisão de ter **duas** formas de carga (74.1, plano)
estava certa.

**Custo por circuito Blazor.** `(126,4 − 63,9) / ~255 circuitos vivos ≈ 0,245 MB/circuito`,
usando o não-descartável máximo do `web` sob carga de circuitos (**126,4 MB**,
`load-web2-report.md`) contra o baseline ocioso (**63,9 MB**, `idle-report.md`) e a
contagem de circuitos concorrentes sustentados durante essa coleta (~255, reportada pelo
operador a partir da saída do próprio `k6` — não fica registrada no CSV do footprint, que
não tem visão do lado da aplicação Blazor). O resultado **bate com os 0,2–0,3 MiB/circuito**
medidos por método independente na tarefa 58 (`docs/load/README.md`, §7.3: "~0,2–0,3 MiB de
RAM por circuito"). Duas medições independentes, dois métodos diferentes, convergindo no
mesmo número — reforça que nem um nem outro é coincidência de medição.

**Pico de build.** As três janelas leram **7–8 MB** de RSS somado de processos de build
(`dotnet build/publish/restore`, `MSBuild`, `VBCSCompiler`) porque nenhuma delas pegou um
build em andamento — é esperado, `run-footprint.sh` roda fora do ciclo de deploy. O número
de referência **anterior** às mudanças da 74.0 é **610 MB de RSS com 82 MB de RAM
disponível e load 13,2** em 1 vCPU, medido nesta VPS antes do swapfile existir
(`reference_vps_deploy.md`, registrado na memória do projeto) — a comparação **pós-74.0**
(Dockerfiles enxutos, `--no-cache` removido) só sai na janela `deploy`, ainda pendente
(seção 7).

## 6. Host e SonarQube

| Métrica | `idle` | `load-api` | `load-web2` |
|---|---|---|---|
| mem. usada MB (p50/p95/máx) | 909,0/936,0/941,0 | 1265,0/1350,0/1380,0 | 1124,0/1189,0/1207,0 |
| mem. disponível MB (p50/p95/máx) | 1058,0/1071,0/1079,0 | 702,0/1046,0/1054,0 | 842,0/899,0/907,0 |
| swap usado MB (p50/p95/máx) | 36,0/37,0/37,0 | 57,0/58,0/58,0 | 58,0/58,0/58,0 |
| load1 (p50/p95/máx) | 0,1/0,3/0,8 | 10,1/29,5/36,3 | 0,8/1,7/2,8 |
| load5 (p50/p95/máx) | 0,1/0,2/0,2 | 9,5/17,2/18,3 | 0,9/1,1/1,2 |
| load15 (p50/p95/máx) | 0,1/0,1/0,1 | 4,4/9,2/9,4 | 1,2/1,3/1,4 |
| disco usado % (/) | 26,0 | 26,0 | 26,0 |

Total de memória da VPS: **1967 MB** (`free -m`, confirmado ao escrever este documento).

**SonarQube (`tesouro-direto-sonar`)**: **parado** (`docker ps -a` → `Exited (0) 4 months
ago`, criado em `2026-03-25`) — custa **0 MB hoje**, confirmado nas três janelas
(`### SonarQube` de cada relatório). Se precisasse voltar a rodar, o parágrafo original da
tarefa 74 já registrava a estimativa de JVM+Elasticsearch ≥1,5 GB — não caberia nesta
máquina de 1967 MB ao lado dos outros 8 containers.

**`mysql-weducate`/`weducate-app`** (outra aplicação, não deste projeto) **foram removidos
na 74.0** a pedido do dono — liberaram **+351 MB** de RAM (disponível foi de 698 MB para
~1035 MB, registrado em `docs/PLANO.md`, "Feito 74.0") e fecharam de quebra um MySQL
publicado em `0.0.0.0:8806`. É por isso que os números deste documento são melhores do que
os do levantamento inicial da tarefa (que ainda contava com o `weducate` de pé).

## 7. Pendências honestas

- **Janelas `cold` e `deploy` ainda não coletadas.** Saem do merge deste próprio PR (o
  script já suporta as duas — `run-footprint.sh cold` e `run-footprint.sh deploy` — só
  faltou rodar). Sem `deploy`, a comparação do pico de build pós-74.0 contra os 610 MB
  (seção 5) fica pendente.
- **A composição de memória durante a importação das 6h UTC não foi capturada.** Nenhuma
  das três janelas cobre esse horário (seção 5); só a marca d'água `mem.peak = 269 MB` do
  `db` a representa, e por isso o orçamento (seção 4) dimensiona o `db` contra ela, não
  contra o pico das janelas medidas.
- **A primeira tentativa da janela de circuitos foi invalidada e descartada.** Registrada
  em `/var/tmp/footprint/load-web-report.md` (CSV: `/var/tmp/footprint/load-20260810-100941.csv`,
  rótulo "k6 circuits Blazor ramp ate 400 VUs") — o rate limit da borda havia sido elevado
  só para `zone=api`, mas os circuitos Blazor entram por `zone=web`, que seguia no limite
  original de 10 r/s. Resultado, reportado pelo operador a partir da saída do próprio `k6`
  (não persistido no CSV do footprint): **101.433 respostas 429**, `blazor_circuit_opened`
  = **459** e **nenhum circuito concorrente real** — o `k6` tentava abrir circuitos e batia
  no limitador antes de completar o handshake. O sintoma ficou visível mesmo no footprint:
  o `web` nessa janela inválida chegou só a 101,9 MB de não-descartável (máximo), contra
  126,4 MB na coleta corrigida (`load-web2-report.md`) — evidência de que a tentativa
  inválida mediu o container **ocioso atrás do rate limiter**, não sob carga real. Lição
  registrada: **medir "sob carga" sem confirmar que a carga chegou mede o container
  ocioso**, não o cenário que se queria medir. A segunda tentativa corrigiu subindo também
  `zone=web` para 100 r/s (`load-web2-report.md`), e é essa a coleta usada em todo este
  documento.
- **`blazor_circuits_live` não serve para validar concorrência real de circuitos**, e por
  pouco essa métrica não escondeu o problema acima. É um `Gauge` do k6
  (`tests/load/site/circuits.js:8`), e cada VU chama `.add(1)` ao abrir o circuito
  (linha 114) ou `.add(0)` ao falhar (linha 80) — um `Gauge` do k6 registra o **último**
  valor setado por qualquer VU, nunca soma entre VUs concorrentes. Por construção, o valor
  de qualquer amostra dessa métrica é sempre 0 ou 1, nunca refletindo quantos circuitos
  estão de fato abertos ao mesmo tempo. A concorrência real exige instrumentação do **lado
  do servidor** (contador de circuitos ativos no `app`/`web`), o que fica como pendência
  para a tarefa **74.5** (que já tem "medir, não extrapolar" como critério para os
  circuitos Blazor).

## 8. O que a fase 74.2 precisa entregar

O gate numérico: fechar **~561 MB** no bloco INFRA (seção 4), ou a divisão de blocos muda
(o próprio plano já previa essa segunda saída, item (v) da escada abaixo). Escada de
fallback pré-acordada (`docs/PLANO.md`, 74.3), na ordem já combinada, com o que cada degrau
plausivelmente rende segundo o medido aqui:

1. **Prometheus: `scrape_interval` 60s + retenção 7d/256MB.** Não fecha parte relevante do
   estouro atual — o `prometheus` já está confortavelmente dentro do orçamento mesmo pela
   regra de folga (limite calculado 114 MB, pico medido sob carga só 50,2 MB). Este degrau
   é preventivo, para quando o self-scrape da própria 74.2 for ligado (hoje o Prometheus
   **não** se auto-monitora — não dá para medir esse efeito sem o self-scrape já ligado).
   Sem re-medição, não dá para estimar em MB.
2. **Loki: retenção 7d + limites de ingestão + `GOMEMLIMIT`.** Maior alavanca isolada
   disponível — o `loki` é quem mais estoura o orçamento provisório (limite calculado
   266 MB contra 96 MB provisionados, seção 5) e quem mais reage à carga (dobra sob
   tráfego). Não dá para estimar quanto o tuning efetivamente derruba: este documento só
   registra o baseline sem tuning nenhum. Ganho real só aparece re-medindo pós-74.2.
3. **node-exporter: só os coletores consumidos.** Rende pouco em termos absolutos — o
   container já é minúsculo (não-descartável nunca passou de 10,1 MB nas três janelas);
   desabilitar coletores não usados dificilmente derruba isso muito mais. O ganho real
   depende de revisar a **regra de folga** (achado da seção 5: piso de `+64 MB` é
   desproporcional para container deste tamanho), não do tuning do próprio coletor.
4. **Grafana: `GOMEMLIMIT` agressivo.** `grafana` também dobra sob carga (107,2 MB idle →
   144,5 MB sob carga, limite calculado 209 MB) — GOMEMLIMIT tende a cortar RSS de Go sob
   pressão, mas quanto isso reduz o pico real não foi medido aqui; não dá para estimar sem
   re-medição pós-74.2.
5. **Tirar o `db` do bloco INFRA, ou criar um terceiro bloco.** Sozinho, tiraria até
   288 MB do bloco (o limite calculado do `db`), reduzindo o excesso de 561 para ~273 MB —
   ainda não fecharia a conta sozinho, mas é o maior degrau isolado depois do `loki`. É a
   "resposta honesta" que o plano já registrava: o `db` (e seus 269 MB de `mem.peak`, ainda
   maior que o limite calculado aqui) talvez simplesmente não caiba dentro de 25% junto
   com prometheus/grafana/loki/promtail/node-exporter no mesmo teto.
6. **Último recurso: derrubar Loki+promtail, voltar a `journald`.** Juntos somam 266+102 =
   368 MB de limite calculado — o maior degrau isolado de todos. Combinado com (5), fecha a
   conta com folga (561 − 288 − 368 < 0). O custo é perder busca de log agregada, por isso
   é o último degrau da escada, não o primeiro.

Onde este documento diz "não dá para estimar", é porque a medição de hoje é o baseline
**sem** nenhum tuning aplicado — inventar um número de melhoria projetada seria menos
honesto do que admitir a lacuna e deixar a re-medição da 74.2 preencher.
