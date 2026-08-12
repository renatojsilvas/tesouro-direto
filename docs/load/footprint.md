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

As **quatro janelas** previstas pelo plano (`cold`, `idle`, `load`, `deploy`) estão
coletadas, em **cinco coletas**: a `load` foi medida em duas formas, carga de API e carga
de circuitos Blazor, que estressam containers diferentes. Daí este documento ter cinco
tabelas para quatro janelas — onde o texto diz "cinco", está falando de coletas. Todas
rodaram com
[`run-footprint.sh`](../../tests/load/profiling/run-footprint.sh) na VPS de produção,
leitura pura. Os CSVs brutos **ficam só na VPS** (nunca sobem para o repo — o deploy faz
`git reset --hard` e os apagaria).

As duas últimas (`deploy` e `cold`) exigiram uma **janela de manutenção deliberada**, e o
motivo é um efeito colateral da própria 74.0. O plano supunha que elas sairiam de graça no
merge do PR desta fase; não saem. Os dois Dockerfiles copiam apenas `src/*/*.csproj` e
`src/` (`Dockerfile:3,12`), este PR não toca `src/`, e a 74.0 removeu o `--no-cache`
(`.github/workflows/deploy.yml:198`) — então todas as camadas dão cache hit, a imagem sai
com o mesmo ID e `docker compose up -d` **não recria container nenhum**, exceto
`prometheus` e `grafana`, que o workflow força (`deploy.yml:203`). Um deploy só-de-docs não
tem build para medir nem startup para observar.

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

### 3.4 `deploy` — deploy real com mudança de código, 120s @ 5s

CSV bruto: `/var/tmp/footprint/deploy-20260811-013452.csv`. Amostras: 25 ciclos, 200 linhas
de container. Janela real: 120s (01:34:55 → 01:36:55 UTC). Critério de parada registrado
pelo próprio script: *estabilidade (6 amostras consecutivas pós-recriação, sem build
ativo); recriação de container observada na janela (ID diferente do snapshot inicial);
processo de build observado: sim*.

Ciclo medido, na ordem: linha de base de 20s → alteração no-op num arquivo de `src/` (para
invalidar a camada `COPY src/`, que é o que um deploy com código de verdade faz) →
`docker compose build` (68s) → `docker compose up -d` → espera de `/health/ready`.

| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | CPU% (p50/p95/máx) |
|---|---|---|---|---|---|---|---|---|---|---|
| tesouro-direto-app | 2 (SIM) | 97.9/168.0/168.0 | 242.5 | 43.2/78.7/78.7 | 55.2/84.9/84.9 | 42.0/64.0/64.0 | 3.7/3.8/3.8 | 2.5/2.6/2.6 | 80.8/146.4/146.4 | 1.6/11.1/47.6 |
| tesouro-direto-db | 1 (não) | 32.0/47.1/47.8 | 269.0 | 0.7/4.8/4.8 | 23.0/42.9/43.3 | 5.6/28.1/28.1 | 2.1/2.6/2.6 | 1.1/1.3/1.3 | 13.0/30.9/30.9 | 0.9/1.7/2.5 |
| tesouro-direto-grafana | 1 (não) | 180.1/195.2/195.2 | 207.1 | 118.8/125.6/125.6 | 55.8/74.5/74.7 | 0.0/0.0/0.0 | 4.0/4.2/4.2 | 2.7/2.9/2.9 | 122.7/129.8/129.8 | 2.0/4.5/5.6 |
| tesouro-direto-loki | 1 (não) | 124.8/149.4/152.5 | 321.1 | 89.7/92.5/92.8 | 33.0/62.7/62.7 | 0.0/0.0/0.0 | 2.2/2.5/2.5 | 1.3/1.6/1.6 | 91.9/94.7/95.0 | 1.2/2.4/2.6 |
| tesouro-direto-node-exporter | 1 (não) | 10.8/12.1/12.2 | 19.2 | 8.8/9.0/9.0 | 1.9/2.5/2.5 | 0.0/0.0/0.0 | 0.9/0.9/0.9 | 0.6/0.7/0.7 | 9.6/9.9/9.9 | 0.0/1.0/1.2 |
| tesouro-direto-prometheus | 1 (não) | 54.7/96.7/97.4 | 109.2 | 38.9/44.6/45.4 | 16.4/49.7/49.7 | 0.0/0.0/0.0 | 1.3/2.1/2.1 | 0.5/1.3/1.3 | 40.2/46.8/47.5 | 0.4/0.7/1.0 |
| tesouro-direto-promtail | 1 (não) | 30.9/40.0/40.0 | 72.6 | 13.1/18.5/18.5 | 15.5/18.9/18.9 | 0.0/0.0/0.0 | 2.4/2.6/2.6 | 1.9/2.0/2.0 | 15.5/21.1/21.1 | 0.3/0.5/0.8 |
| tesouro-direto-web | 1 (não) | 106.4/143.2/143.2 | 145.8 | 57.1/79.5/79.5 | 45.3/61.2/61.2 | 29.8/37.4/37.4 | 2.3/2.3/2.3 | 1.3/1.3/1.3 | 89.3/119.2/119.2 | 0.1/0.2/0.5 |

Host (`deploy`): mem. usada 1151,0/1434,0/1444,0 MB; disponível 816,0/1078,0/1088,0;
**swap 181,0/288,0/293,0**; load1 7,5/11,8/11,8; disco 26,0%.

**Pico de build nesta janela: 200/834/840 MB** de RSS somado (p50/p95/máx) — ver seção 5.

Só o `app` foi recriado. O `web` foi **rebuildado e não recriado**: a alteração foi num
arquivo do projeto da API, os dois Dockerfiles copiam `src/` inteiro (então as duas imagens
foram reconstruídas), mas o publish do `web` não inclui a API — o conteúdo final saiu
idêntico, a imagem manteve o mesmo ID e o compose não teve o que trocar. **Rebuildar não
implica recriar.**

### 3.5 `cold` — recriação dos 8 containers, 300s @ 5s

CSV bruto: `/var/tmp/footprint/cold-20260811-014031.csv`. Amostras: 60 ciclos, 471 linhas
de container. Janela real: 294s (01:40:33 → 01:45:27 UTC), encerrada por prazo (a janela
`cold` não tem parada antecipada).

O rebuild limpo que restaurou a imagem de `origin/main` rodou **fora** da janela, de
propósito: na primeira versão do procedimento ele ficava dentro, e um `dotnet publish`
disputando 1 vCPU com os containers subindo mediria "startup sob carga de build", não
startup. A janela cobre linha de base de 20s → `docker compose up -d --force-recreate` nos
oito → espera de saúde → regime.

| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | CPU% (p50/p95/máx) |
|---|---|---|---|---|---|---|---|---|---|---|
| tesouro-direto-app | 2 (SIM) | 102.4/125.4/125.4 | 128.1 | 39.7/42.5/42.5 | 59.7/80.1/80.1 | 49.8/52.0/52.0 | 2.8/2.9/2.9 | 1.8/1.8/1.8 | 92.6/97.3/97.4 | 2.2/8.2/16.9 |
| tesouro-direto-db | 2 (SIM) | 36.5/38.1/38.1 | 269.0 | 7.9/7.9/7.9 | 24.9/26.6/26.6 | 13.7/13.7/13.7 | 3.7/3.7/3.7 | 2.5/2.5/2.5 | 25.2/25.2/25.2 | 0.9/1.8/2.5 |
| tesouro-direto-grafana | 2 (SIM) | 286.2/296.7/301.2 | 302.6 | 116.2/126.8/131.4 | 164.9/165.2/165.2 | 0.0/0.0/0.0 | 4.9/4.9/4.9 | 3.7/3.7/3.7 | 121.1/131.8/136.3 | 1.8/4.3/23.3 |
| tesouro-direto-loki | 2 (SIM) | 123.2/128.0/128.0 | 321.1 | 75.1/80.8/80.8 | 45.9/51.4/51.5 | 0.0/0.0/0.0 | 1.1/2.2/2.2 | 0.5/1.3/1.3 | 76.2/82.0/82.0 | 1.0/2.2/3.5 |
| tesouro-direto-node-exporter | 2 (SIM) | 15.3/15.6/15.6 | 19.2 | 7.0/7.5/7.5 | 7.5/7.8/7.8 | 0.0/0.0/0.0 | 0.7/0.9/0.9 | 0.5/0.6/0.6 | 7.7/8.4/8.4 | 0.0/0.8/1.4 |
| tesouro-direto-prometheus | 2 (SIM) | 97.3/100.8/101.8 | 109.2 | 28.8/38.4/39.6 | 67.6/70.8/71.1 | 0.0/0.0/0.0 | 1.1/1.3/1.3 | 0.5/0.5/0.5 | 29.9/39.7/41.0 | 0.3/0.7/0.8 |
| tesouro-direto-promtail | 2 (SIM) | 66.2/75.1/75.1 | 75.4 | 16.0/16.2/16.3 | 49.3/57.9/57.9 | 0.0/0.0/0.0 | 0.9/2.4/2.4 | 0.4/1.9/1.9 | 16.9/17.2/17.2 | 0.3/0.5/0.6 |
| tesouro-direto-web | 2 (SIM) | 34.2/100.6/100.6 | 145.8 | 12.3/52.6/52.6 | 19.9/44.8/44.8 | 16.1/28.9/28.9 | 1.9/2.3/2.3 | 1.1/1.3/1.3 | 30.3/83.7/83.7 | 0.2/0.8/1.4 |

Host (`cold`): mem. usada 847,0/907,0/940,0 MB; disponível 1119,0/1189,0/1452,0;
swap 51,0/177,0/177,0; load1 0,9/1,3/1,4; disco 26,0%.

Pico de build nesta janela: **0/0/0 MB** — confirmação de que o rebuild ficou de fato fora
da janela.

Nas cinco coletas não houve `OOM kills (cgroup)`, reinício por política ou
`OOMKilled=true` em container nenhum.

O log operacional da janela registra `web=000` e `docs=000` nas checagens finais: o `curl`
rodou ~1s depois de o container `web` subir (`Up 1 second`), antes de o Kestrel aceitar
conexão. Reconferido alguns minutos depois — `web=200` e `docs=200`, e o `container_id` do
`web` não muda mais no CSV. É ruído de tempo do procedimento, não sintoma.

### 3.6 Consolidado — máximo de cada container entre as cinco coletas

| Container | não-descartável máx entre janelas (MB) | Janela onde ocorreu | mem.peak máx entre janelas (MB) |
|---|---|---|---|
| `app` | 219,7 | load-api | 242,5 |
| `db` | 221,7 | load-api | 269,0 |
| `grafana` | 144,5 | load-api | 302,6 |
| `loki` | 201,7 | load-api | 321,1 |
| `node-exporter` | 10,1 | load-web2 | 19,2 |
| `prometheus` | 50,2 | load-web2 | 109,2 |
| `promtail` | 37,5 | load-api | 75,4 |
| `web` | 126,4 | load-web2 | 145,8 |

**A coluna que dimensiona limite não mudou.** Nenhuma das duas janelas novas estabeleceu
máximo novo de não-descartável para container nenhum — todas as oito linhas seguem vindo de
`load-api` ou `load-web2`. O orçamento da seção 4 fica **exatamente** como estava; as duas
janelas custaram uma janela de manutenção e não moveram um MB da conta. Isso é resultado,
não anticlímax: ver seção 5, onde as duas viram um achado cada.

`mem.peak` é uma marca d'água **desde o start do container** (não zera entre janelas, só
em recriação/restart-por-política) — por isso pode exceder o pico visto nas janelas medidas
aqui (caso do `db`, ver seção 5). Três valores subiram em relação à versão anterior desta
tabela, e os três merecem leituras diferentes:

- **`grafana` 207,1 → 302,6.** Pico **observado dentro** da janela `cold`, com a composição
  medida no instante exato: `anon` 131,4 + `file` 164,8 + `shmem` 0 ⇒ não-descartável
  136,3. O acréscimo é page cache, e isso está verificado, não inferido.
- **`promtail` 72,6 → 75,4.** Também observado dentro da janela `cold`, no container
  recriado.
- **`prometheus` 95,9 → 109,2.** Aqui a marca d'água apenas **apareceu** na janela
  `deploy`: já valia 109,2 na primeiríssima amostra (`memory.current` 96,6 no mesmo
  instante), ou seja, foi estabelecida **antes de qualquer janela medida**. Depois do
  `--force-recreate` da janela `cold`, o container novo não passou de 102,1 em cinco
  minutos. **A composição do momento em que esses 109,2 foram atingidos nunca foi
  observada** — diferente do `grafana`, aqui não há como afirmar se o acréscimo era page
  cache ou não.

## 4. Orçamento: a conta que não fecha

Máquina de **1 vCPU / 1967 MB** ⇒ 25% = **~492 MB** por bloco (1967 × 0,25 = 491,75).
Regra de folga do plano (`docs/PLANO.md`, 74.3): `limite = max(pico × 1,3 ; pico + 64 MB)`,
aplicada aqui sobre o **máximo não-descartável entre as cinco coletas** (coluna da tabela
3.6). Cada limite é arredondado ao MB mais próximo antes de somar o bloco.

Os números desta seção **não mudaram** com as janelas `deploy` e `cold`: nenhuma das duas
estabeleceu máximo novo (tabela 3.6). A conta abaixo é a mesma de quando só havia três
janelas, agora com as cinco previstas pelo plano medidas.

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
desta coleta) e que **nenhuma das cinco coletas reproduziu** — o job Quartz diário de
importação roda às `0 0 6 * * ?` (6h **UTC**; `src/TesouroDireto.Infrastructure/DependencyInjection.cs:154`),
e nenhuma das cinco (02:10–03:09, 09:37–09:52, 10:25–10:40, 01:34–01:36, 01:40–01:45 UTC)
cobre esse horário. Como a composição de memória durante a importação não foi capturada (só
a marca d'água), **dimensionar o `db` contra os 269 MB**, não contra os 221,7 medidos, é a
escolha conservadora e correta até existir uma janela que cubra as 6h UTC.

> **A marca d'água de 269 MB não existe mais, e quem a destruiu foi a janela `cold` deste
> documento.** O `--force-recreate` da seção 3.5 recriou o `db` (criado em
> `2026-08-11T01:40:51Z`), e `memory.peak` é por cgroup: o do container novo começou do
> zero. Verificado na VPS logo depois — `memory.peak` do `db` agora lê **41,2 MB** contra
> os 269,0 de antes. Era consequência previsível de recriar o container e não foi prevista.
>
> O dado não deixou de valer (foi observado, está registrado aqui e nos números de partida
> de 2026-08-09), mas **não pode mais ser reconferido no container vivo**, e é a base do
> limite de 288 MB do `db` na seção 4 — o item mais caro do bloco INFRA. O efeito colateral
> útil: a próxima importação das 6h UTC vai reconstruir a marca d'água **limpa**, medindo só
> aquele evento, em vez das duas semanas de picos acumulados de origem desconhecida que
> produziram os 269. Coletar isso custa uma linha (`cat memory.peak` do cgroup do `db`
> depois das 6h UTC) e dá um número melhor do que o que se perdeu.

**O piso de `+64 MB` da regra de folga é ruim para container pequeno.** Para o
`node-exporter`, cujo não-descartável nunca passou de **10,1 MB** nas cinco coletas (máximo
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

**Pico de build: 840 MB, e o instrumento estava quebrado.** A versão anterior deste
documento afirmava que as três janelas liam "7–8 MB de RSS de processos de build ... porque
nenhuma delas pegou um build em andamento — é esperado". Não era esperado: sem build
rodando, o número certo é **zero**. Os 7–8 MB eram o próprio `awk` do medidor se medindo —
o programa contém os literais `MSBuild` e `VBCSCompiler`, e o `ps` o lista com o texto do
programa nos argumentos, então ele casava consigo mesmo, e em dobro, por casar as duas
regras. Uma explicação plausível ocupou o lugar da investigação, que é exatamente como um
artefato de medição sobrevive numa tabela.

O mesmo `awk` tinha um segundo defeito, na direção oposta: comparava o regex contra a linha
inteira do `ps`, que começa com a coluna de RSS, então `(^|\/)dotnet` **nunca casava com
`dotnet publish`** — o medidor era cego justamente ao processo que mais pesa e só enxergava
`MSBuild`/`VBCSCompiler` por substring nua. Os dois defeitos estão corrigidos
(`run-footprint.sh`, `build_rss_kb`), com o auto-casamento fechado por um sentinela
explícito em vez do truque de classe de caractere, que some para quem editar o arquivo
depois.

Com o instrumento consertado, a janela `deploy` mediu **200/834/840 MB** (p50/p95/máx) de
RSS somado, com dois `dotnet publish` em paralelo (`docker compose build` builda `app` e
`web` juntos) num único vCPU.

**A comparação contra os 610 MB não pode ser feita como antes/depois, e forçá-la seria
pior do que declarar isso.** O número de referência anterior à 74.0 — **610 MB de RSS com
82 MB de RAM disponível e load 13,2**, medido nesta VPS antes de o swapfile existir
(`reference_vps_deploy.md`, na memória do projeto) — vem de outro método, e o método deste
documento acabou de ser encontrado quebrado nas duas direções. Não dá para afirmar que o
pico "subiu de 610 para 840": os dois números não medem a mesma coisa do mesmo jeito.

O que **é** comparável, e mudou muito, é a folga do host no pior instante do deploy:

| | antes da 74.0 | agora (janela `deploy`) |
|---|---|---|
| RAM disponível no pior momento | **82 MB** | **816 MB** |
| load1 | 13,2 | 11,8 |
| swap | não existia | 293 MB em uso |

O swapfile da 74.0 não é enfeite: 293 MB dele foram efetivamente usados durante este
deploy. E o pico de build **não é governado por limite nenhum do compose** — o BuildKit
roda dentro do daemon, fora do cgroup do serviço —, então esses 840 MB continuarão fora do
orçamento 25/25/50 depois da 74.3. A rede de segurança do deploy é o swapfile mais a RAM
livre, não os limites de container.

**A premissa da janela `cold` foi refutada pela própria janela `cold`.** O plano a
justificava assim: "o pico de RSS é no startup, não em regime". Medido, o não-descartável
no startup ficou **abaixo** do regime para os oito containers, sem uma única exceção:

| Container | `cold` (máx) | máximo em regime/carga | razão |
|---|---|---|---|
| `app` | 97,4 | 219,7 | 0,44× |
| `db` | 25,2 | 221,7 | 0,11× |
| `grafana` | 136,3 | 144,5 | 0,94× |
| `loki` | 82,0 | 201,7 | 0,41× |
| `node-exporter` | 8,4 | 10,1 | 0,83× |
| `prometheus` | 41,0 | 50,2 | 0,82× |
| `promtail` | 17,2 | 37,5 | 0,46× |
| `web` | 83,7 | 126,4 | 0,66× |

Consequência prática para a 74.3: **um limite dimensionado pelo pico sob carga não corre
risco de matar o container no boot**, que era a preocupação implícita na quarta janela.
Vale para esta stack, com estes serviços — não é lei geral.

**Mas o `cold` achou outra coisa, no `memory.current`.** O `grafana` sobe a **301,2 MB de
`memory.current` no startup** contra ~195 em regime — 165 MB disso é `file` (page cache,
carregando dashboards e plugins do disco). O não-descartável correspondente é só 136,3 MB.
É o argumento da seção 2 aparecendo em estado puro: quem dimensionasse pelo `memory.current`
daria 301 MB ao Grafana; pelo não-descartável, 136. **Com uma ressalva que a 74.3 precisa
absorver:** um limite fixado exatamente no não-descartável não mata o container (o kernel
recupera page cache antes de invocar o OOM killer), mas transforma esses 165 MB de cache em
pressão de reclaim contínua durante todo o boot — startup mais lento, não morte. A folga da
regra (`×1,3` ou `+64`) cobre parte disso por acidente, não por projeto.

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
ago`, criado em `2026-03-25`) — custa **0 MB hoje**, confirmado nas cinco coletas
(`### SonarQube` de cada relatório). Se precisasse voltar a rodar, o parágrafo original da
tarefa 74 já registrava a estimativa de JVM+Elasticsearch ≥1,5 GB — não caberia nesta
máquina de 1967 MB ao lado dos outros 8 containers.

**`mysql-weducate`/`weducate-app`** (outra aplicação, não deste projeto) **foram removidos
na 74.0** a pedido do dono — liberaram **+351 MB** de RAM (disponível foi de 698 MB para
~1035 MB, registrado em `docs/PLANO.md`, "Feito 74.0") e fecharam de quebra um MySQL
publicado em `0.0.0.0:8806`. É por isso que os números deste documento são melhores do que
os do levantamento inicial da tarefa (que ainda contava com o `weducate` de pé).

## 7. Pendências honestas

- **O deploy medido não é idêntico ao deploy do CI.** A janela 3.4 reproduziu o ciclo à
  mão (`build` → `up -d` → espera de saúde) para poder invalidar a camada `COPY src/` de
  propósito, mas pulou o que o CI faz antes: `git fetch`/`reset --hard`, cópia do
  `tesouro-direto.conf` e `nginx -t && systemctl reload nginx`
  (`.github/workflows/deploy.yml:186-197`). São passos baratos em CPU e memória perto de
  dois `dotnet publish`, e nenhum deles cria container — mas não foram medidos, e por isso
  o número desta janela é o custo do **build + recriação**, não do job de deploy inteiro.
- **O `--force-recreate` da janela `cold` recria os oito de uma vez; o CI não.** No deploy
  real só sobem os containers cuja imagem ou config mudou (tipicamente `app`, mais
  `prometheus`/`grafana` que o workflow força). A janela 3.5 é portanto o **pior caso** de
  startup simultâneo, mais pesada que qualquer deploy real — o que é a direção segura para
  dimensionar, mas convém não citá-la como "o que acontece num deploy".
- **A comparação do pico de build contra os 610 MB continua sem poder ser fechada** — não
  por falta de medição, mas porque os dois números vêm de métodos diferentes e o método
  antigo era comprovadamente defeituoso (seção 5). Fechá-la exigiria re-medir o cenário
  pré-74.0, o que significaria reverter o `--no-cache` e os Dockerfiles num deploy real só
  para produzir o número. Não vale o risco; a folga de RAM do host (82 MB → 816 MB) responde
  à pergunta que importava.
- **A composição de memória durante a importação das 6h UTC não foi capturada.** Nenhuma
  das cinco coletas cobre esse horário (seção 5); a marca d'água `mem.peak = 269 MB` do
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
   container já é minúsculo (não-descartável nunca passou de 10,1 MB nas cinco coletas);
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

---

## 9. Re-medição pós-74.2 (2026-08-12) — o gate da fase, e o que ele muda

Mesmo instrumento, mesmas janelas, mesmo script de carga, mesma borda. O que mudou entre as
duas medições é só o tuning da fase 74.2 (Postgres, Npgsql, MemoryCache, GC/ReadyToRun,
Loki, Prometheus, node-exporter).

### 9.1 Não-descartável máximo por container

| Container | 74.1 | 74.2 | Δ | janela do pico |
|---|---|---|---|---|
| `db` | 221,7 | **70,0** | −151,7 (−68%) | load-api |
| `loki` | 201,7 | **100,9** | −100,8 (−50%) | idle |
| `app` | 219,7 | **190,4** | −29,3 (−13%) | load-api |
| `web` | 126,4 | **120,1** | −6,3 (−5%) | circuitos |
| `prometheus` | 49,4 | **36,8** | −12,6 (−26%) | load-api |
| `promtail` | 37,5 | **26,5** | −11,0 (−29%) | load-api |
| `grafana` | 144,5 | **133,8** | −10,7 (−7%) | idle (**pico de boot**) |
| `node-exporter` | 10,1 | **8,4** | −1,7 (−17%) | load-api |
| **Total** | **1011,0** | **686,9** | **−324,1 (−32%)** | |

> **Correção de uma versão anterior desta seção.** A primeira redação listava `grafana` em
> **97,9** e `loki` em **98,9**, tomando o máximo apenas da janela `load-api`. Está errado:
> o máximo é **entre todas as janelas**, e para esses dois ele está na `idle`. O caso do
> `grafana` não é ruído — é **pico de boot**: p50 96,0 e p95 97,8 contra máximo de 133,8, e a
> janela `idle` começou 13 min depois de o deploy recriar o container. Bate com o que a §5
> já registrava da janela `cold` da 74.1 (`grafana` a 136,3 no boot). O erro foi pego porque
> um limite de 128 MB, dimensionado sobre o número errado, deixou o `grafana` a **99,9% do
> teto com 9131 eventos de reclaim** — o sintoma apareceu na 74.3 e a causa era esta tabela.

Zero OOM kill e zero reinício em todas as janelas.

**O `db` é o resultado que muda a tarefa, e não é cache frio.** O `anon` caiu de 163,9 para
22,1 MB. Memória privada de backend do Postgres é *por conexão*: `max_connections` foi de
100 para 25 e a pool do Npgsql, que podia chegar a 200 conexões somando EF e Dapper, foi
capada em 10. Cortar conexão corta memória privada — os caps não eram cosméticos.

**O `loki` parou de dobrar.** A 74.1 o apontou como "o problema não óbvio" porque ele
acompanhava a rajada de log do nginx sob carga (104,4 → 201,7). Com retenção de 7d, limites
de ingestão e `GOMEMLIMIT`, o pico sob a mesma carga é 98,9.

### 9.2 O orçamento fecha — com observabilidade LOCAL

Aplicando a **folga proporcional** (`pico × 1,3`, sem o piso de `+64 MB`), adotada pelo dono
em 2026-08-12 sobre a recomendação da §5 deste documento:

| Bloco | Serviços (limite aplicado na 74.3) | Total | % da máquina |
|---|---|---|---|
| **APP** | `app` 256 · `web` 160 | **416 MB** | 21,1% |
| **INFRA** | `db` 128 · `grafana` 160 · `loki` 132 · `prometheus` 48 · `promtail` 36 · `node-exporter` 16 | **520 MB** | 26,4% |
| **Soma** | | **936 MB** | **47,6%** |

**A promessa que se sustenta é a dos 50% livres, não o corte 25/25.** Os dois blocos somam
936 MB dos 984 disponíveis — sobram 48 MB e a metade livre da máquina está preservada. Mas
o **INFRA legitimamente precisa de mais que o APP** (26,4% contra 21,1%): é assim que a
carga se distribui nesta stack, e forçar 25/25 significaria espremer o `grafana` abaixo do
seu pico de boot medido. O estouro de 561 MB apurado na §4 virou folga, e **a migração para
o Grafana Cloud deixa de ser necessária: passa a ser opcional.**

**A escolha da regra de folga é o que decide, não o consumo.** Com a regra original
(`max(pico × 1,3; pico + 64 MB)`) o bloco INFRA daria **723 MB** e não caberia. O piso fixo
dava 72 MB de limite ao `node-exporter`, que nunca passou de 8,4 — folga que não protege
nada e ocupa orçamento de quem precisa.

### 9.3 Baseline k6: não houve perda, houve ganho

O gate da 74.2 foi afrouxado pelo dono para aceitar até 10% de queda de throughput em troca
de aperto agressivo de `db` e `app`. **Não foi preciso usar essa margem.** Mesmo script,
mesma borda (`zone=api` a 100 r/s), 3 runs de cada lado, comparação pela mediana entre runs:

| | 74.1 | 74.2 | Δ |
|---|---|---|---|
| Vazão **atendida** (req/s) | 58,3 | **69,1** | **+18,6%** |
| Latência mediana (ms) | 386,9 | **272,8** | **−29,5%** |
| Latência p95 (ms) | 2505,4 | **1453,4** | **−42,0%** |

> Vazão **atendida** = `http_reqs × (1 − http_req_failed)`. A vazão *total* do k6 é métrica
> enganosa aqui: ela conta os 429 da borda, que são baratos e rápidos, então **mais rejeição
> infla a vazão**. Por essa métrica errada o ganho pareceria +92,6%.

**O achado estrutural vale mais que as medianas: o sistema parou de degradar run a run.**

| run | 74.1 (atendidas/s · med) | 74.2 (atendidas/s · med) |
|---|---|---|
| 1º | 67,8 · 270 ms | 70,1 · 257 ms |
| 2º | 58,3 · 387 ms | 68,1 · 273 ms |
| 3º | 55,6 · 579 ms | 69,1 · 289 ms |

Antes, três rampas seguidas derrubavam a vazão em 18% e **dobravam** a latência mediana —
o sistema acumulava pressão de um run para o outro. Depois, fica plano. É o comportamento
esperado de capar a pool de conexões e limitar o cache: sem teto, cada rampa deixava para
trás conexões e entradas de cache que a seguinte herdava.

### 9.4 Ressalvas honestas desta re-medição

- **O `db` está projetado no teto, não medido nele.** O pico medido foi 70,0 MB, mas o
  `shmem` (42,4) ainda cresce até os 64 MB de `shared_buffers` conforme as páginas são
  tocadas. O limite de ~120 MB usa uma projeção de ~92 MB, não o número medido.
- **Nenhuma janela satura o `app`.** 69,6% das requisições levaram **429 na borda**, nos
  dois lados da comparação — o limitador rejeita antes de a aplicação ver a carga. A
  comparação 74.1×74.2 é justa (mesma condição), mas nenhuma das duas mede o teto real da
  aplicação. Isso é escopo da 74.5.
- **A concorrência real de circuitos Blazor continua não medida.** O ramp abriu 919
  circuitos (confirmados por 919 respostas `101` no log do nginx) e sustentou sessões de
  ~76s com 399 VUs, mas a concorrência simultânea só pode ser estimada entre ~166 e ~400 —
  faixa larga demais para fechar o custo por circuito. É a mesma pendência que a §7 já
  registrou para a 74.5, e o `blazor_circuits_live` do k6 não serve (é um `Gauge`: sempre 0
  ou 1). Por isso os −5% do `web` **não** devem ser lidos como ganho: podem ser só menos
  circuitos simultâneos que na 74.1 (~255 estimados lá).
- **A janela `idle` tinha viés de uptime a favor do número novo** (containers com ~1h de
  vida contra semanas na 74.1). Os números desta seção usam as janelas **sob carga**, onde
  o viés é muito menor, justamente por isso.
- **O pico de build leu 0/0/0 MB na janela `idle`**, que é o valor correto sem build
  rodando — confirma que a correção do `awk` que se media a si mesmo (§5) pegou.

### 9.5 CPU medida pós-74.2 — e por que ela decidiu a forma dos limites da 74.3

O documento não tinha tabela de CPU pós-tuning; esta lacuna foi apontada durante a 74.3, e
é dela que saem os números que mudaram a decisão daquela fase.

| Container | `load-api` (p50/p95/máx) | `circuitos` (p50/p95/máx) | pico |
|---|---|---|---|
| `app` | 26,8 / 44,0 / **51,8** | 1,5 / 5,0 / 10,3 | **51,8%** |
| `db` | 1,6 / 33,5 / **46,2** | 0,9 / 2,9 / 12,8 | **46,2%** |
| `web` | 0,2 / 0,2 / 0,4 | 0,6 / 11,6 / **21,0** | **21,0%** |
| `promtail` | 1,0 / 4,5 / **5,8** | 0,4 / 0,6 / 0,9 | 5,8% |
| `loki` | 1,4 / 2,3 / **4,8** | 0,7 / 1,8 / 3,4 | 4,8% |
| `grafana` | 0,4 / 1,1 / 2,4 | 0,4 / 1,4 / **3,4** | 3,4% |
| `prometheus` | 0,0 / 0,3 / 0,3 | 0,0 / 0,3 / **0,5** | 0,5% |
| `node-exporter` | 0,0 / 0,1 / 0,1 | 0,0 / 0,1 / 0,1 | 0,1% |

**A memória cabe; a CPU não cabe.** O bloco APP mediu `app` + `web` = **0,73 vCPU** contra
um teto de 0,25 — quase 3× acima; com a folga de ×1,3, 0,95, praticamente a máquina inteira.
O INFRA mediu 0,61 contra 0,25. A provisão do plano (`app` 0,18 · `db` 0,10 · `web` 0,07)
representaria cortes de 65–78% sobre o pico medido.

**E o corte de CPU não é proporcional.** O CFS é quota por janela de 100 ms: com
`cpus: 0.18`, uma requisição que precisa de 40 ms de CPU é estrangulada ao menos duas vezes
e ganha 60–85 ms de espera pura — p95/p99 pioram **mais que proporcionalmente**. O dono
tinha autorizado até 10% de perda de throughput na 74.2; um teto rígido nesses valores
passaria longe disso.

**Por isso a 74.3 desviou do plano:** memória com teto rígido (é estado — um processo pode
retê-la e não devolver, e um vazamento leva a máquina), CPU por **peso** (`cpu_shares`) com
teto folgado só contra loop desgovernado. Ciclo de CPU **não se estoca**: ciclo não usado é
perdido, não fica reservado para o vizinho, e quando outra aplicação precisar dele o peso é
aplicado naquele instante — que é exatamente quando a garantia importa.

> Observação de método: as colunas de CPU do `run-footprint.sh` marcam
> `throttling: n/d (sem período de CPU registrado)` em todas as janelas desta re-medição —
> correto, porque **não havia limite de CPU aplicado ainda**. A partir da 74.3 esse campo
> passa a ter valor, e `nr_throttled`/`throttled_usec` viram o sinal falsificável de que a
> quota é a restrição vinculante.
