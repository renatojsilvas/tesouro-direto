# Profiling sob carga (isolar o gargalo de capacidade)

O baseline em produção (seção 7 de [`README.md`](README.md)) mostrou que os fluxos pesados
(`titulos` com a dupla chamada de ETag, `historico` paginado) **saturam em latência (~7 s de
p95) a ~21–23 req/s**, sem 5xx e antes de bater no limite da borda (nginx). Esse número diz
*quando* satura, mas não diz *por quê*: pode ser CPU, thread-pool do .NET esgotado
(starvation), pressão de GC, ou o Postgres. Este documento descreve como reproduzir esse
joelho localmente, com profiling ligado, para isolar a causa.

Ferramentas novas, isoladas em `tests/load/profiling/` e `docker-compose.profiling.yml`.
Nada em `src/` foi tocado.

## 1. Por que rodar local com CPU limitada

A VPS de produção é pequena (poucos vCPUs). Rodar o profiling localmente **sem** limitar CPU
não reproduz o joelho — a máquina de desenvolvimento tem CPU de sobra e o mesmo cenário passa
liso (o `README.md`, seção 7, já registra isso: localmente `titulos.js` deu p95 de 262 ms a
200 VUs, ~874 req/s, uma ordem de grandeza melhor que produção). Para reproduzir o gargalo é
preciso **artificialmente limitar CPU/memória do container `app`** para o mesmo patamar da VPS
— é isso que `docker-compose.profiling.yml` faz (`deploy.resources.limits.cpus`/`.memory`,
controláveis por `PROFILE_CPUS`/`PROFILE_MEM`, default `1`/`1g`). Desde a fase 74.3, o
`docker-compose.yml` base já traz `deploy.resources.limits` para `app`/`db` — este overlay
sobrescreve só o `app` para o patamar escolhido pelo operador, e usa a forma canônica do
Compose Spec de propósito: o Compose v2 rejeita o projeto se um overlay usar a forma legada
(`cpus`/`mem_limit`) com valor distinto da forma canônica para o mesmo serviço.

## 2. O que cada peça captura

- **`/metrics` do próprio app** (prometheus-net, já exposto — não precisa de sidecar): o
  `run-profile.sh` faz polling e registra os EventCounters do runtime que importam:
  `system_runtime_threadpool_queue_length` (starvation), `system_runtime_threadpool_thread_count`,
  `process_cpu_seconds_total` (CPU acumulada), `system_runtime_total_pause_time_by_gc_total`
  (tempo em pausa de GC), `system_runtime_gc_heap_size`. Coletado no início (`T0`), a cada 4 s
  durante a carga, e no fim (`T1`), permitindo derivar deltas.
- **`docker stats`** do container `app`: CPU% real do container (com o `cpus` limitado, ~100%
  = 1 core saturado). Complementa o `process_cpu_seconds_total` (que na prática pode subestimar
  em container/Docker Desktop).
- **Postgres com `pg_stat_statements`**: o overlay liga a extensão via `shared_preload_libraries`
  no `command` do serviço `db` (única mudança nesse serviço). `run-profile.sh` cria a extensão,
  zera as estatísticas antes do run e lê o top de queries por tempo total depois.
- **k6 (`tests/load/profiling/steady.js`)**: carga **sustentada** (`constant-vus`, não rampa) —
  o objetivo aqui não é achar o teto, é manter carga estável (~24 VUs) tempo suficiente para os
  sinais terem estabilidade.

> Nota: uma tentativa inicial usou o `dotnet-monitor` como sidecar (CPU trace + counters). No
> Docker Desktop ele entrou em crash-loop por conflito no socket de diagnóstico compartilhado, e
> o `DOTNET_DiagnosticPorts` injetado no `app` desestabilizava o processo sob carga. A via acima
> (`/metrics` que o app já expõe) entrega os mesmos sinais de thread-pool/GC/CPU sem tocar a
> imagem do app nem depender do sidecar.

## 3. Subir o stack de profiling

```bash
PROFILE_CPUS=1 PROFILE_MEM=1g \
  docker compose -f docker-compose.yml -f docker-compose.profiling.yml up -d db app

docker exec -i tesouro-direto-db psql -U postgres -d tesouro_direto \
  < tests/TesouroDireto.E2E.Tests/seed.sql
```

`-U postgres`: role admin (79-A.2) — o seed roda TRUNCATE/INSERT direto nas tabelas, não com a
credencial `td_app` da aplicação.

`PROFILE_CPUS`/`PROFILE_MEM` têm default `1`/`1g` no overlay; ajuste para o hardware real da
VPS se quiser reproduzir mais fielmente. O seed usa o mesmo arquivo dos testes E2E — nunca rode
profiling contra um banco vazio ou só com títulos vencidos (os fluxos `historico`/`preco-atual`/
`simulador` dependem de `pickCodigo()` achar um título não vencido).

## 4. Rodar

```bash
API_URL=http://localhost:5000 API_KEY=<service-key> \
  bash tests/load/profiling/run-profile.sh titulos 24 90s
```

Argumentos posicionais com default: `FLOW` (`titulos`/`historico`/`preco-atual`/`simulador`,
default `titulos`), `VUS` (default `24`), `DURATION` (default `90s`). `API_URL` e `API_KEY` são
obrigatórias, sem default — o script bloqueia `dadosdotesourodireto.com.br` a menos que
`ALLOW_PROD=1` seja definido. O script checa `GET /health` antes de prosseguir.

> A `service key` costuma conter caracteres especiais — passe-a por variável de ambiente
> (`API_KEY=...`), nunca inline num header de `curl` no shell (o shell quebra o valor e você vê
> `401` espúrio). O k6 (`steady.js`) monta o header corretamente a partir de `API_KEY`.

### Rodando contra a VPS

O mesmo cenário roda contra produção trocando `API_URL` e definindo `ALLOW_PROD=1`. **Não é
recomendado como rotina** — profiling é invasivo e o script zera `pg_stat_statements` do banco.
Só pontualmente, com o dono ciente.

## 5. Artefatos e como ler cada um

Tudo cai em `scratchpad/` (raiz do repo, no `.gitignore`), prefixado por `profile-<FLOW>`:

| Arquivo | Conteúdo | Como ler |
|---|---|---|
| `profile-<FLOW>-samples.txt` | Amostras de `/metrics` (T0, a cada 4 s, T1) + linhas `DSTAT` do `docker stats` | Ver contadores-chave abaixo; derivar deltas T0→T1 de CPU e GC. |
| `profile-<FLOW>.json` | `--summary-export` do k6 (p95/p99, taxa de erro, `etag_304_rate` no `titulos`) | Comparar com o baseline (seção 7 do `README.md`). |
| `profile-<FLOW>.log` | stdout/stderr do `k6 run` | Sanidade (erros de script, contagem de iterações). |
| `profile-<FLOW>-pgstat.txt` | Top queries por `total_exec_time` (`pg_stat_statements`, zerado antes) | Se 1-2 queries dominam `total_ms`/`pct`, o gargalo é DB. |

Contadores-chave em `-samples.txt`:

- **`threadpool_queue_length` alto e crescente** → thread-pool starvation (I/O síncrono
  bloqueando threads, ou pool subdimensionado).
- **`threadpool_thread_count` subindo continuamente** → runtime injetando threads para compensar
  starvation.
- **`docker stats` ~100% do core (`cpus`) com queue baixa** → CPU-bound puro.
- **`total_pause_time_by_gc` com delta grande vs. wall** → pressão de GC (alocação alta).
- **Nenhum no vermelho, mas `pg_stat_statements` com 1-2 queries dominando** → o gargalo é o DB.

## 6. Resultado observado (titulos, 24 VUs, 90 s, 1 CPU)

Cruzando os quatro sinais, o gargalo é **o banco**:

| Sinal | Medição | Veredito |
|---|---|---|
| Thread-pool | `queue_length` ~0 o tempo todo; 6 threads estáveis | **Não é starvation** |
| GC | pausa ≈ 6,4 % do wall | Secundário (alocação por request) |
| CPU | `docker stats` ~100 % de 1 core | Ocupado, mas atende (Postgres local é rápido) |
| DB (`pg_stat_statements`) | **~129 mil execuções** de 1 query = **79 %** do tempo de DB; `DISCARD ALL` = 21 % | **É o gargalo** |

A query dominante é a de versão do ETag —
`src/TesouroDireto.Infrastructure/Http/ContentVersionProvider.cs`
(`SELECT max(data_base), count(*) precos, count(*) titulos`), chamada pelo
`ConditionalGetFilter` em **toda** requisição de leitura para montar o ETag, **sem cache**. Ou
seja: mesmo quando a resposta é `304 Not Modified`, o servidor consulta o banco para calcular a
version. No Postgres local isso custa ~0,09 ms (aguenta ~1440 req/s), mas na VPS (Postgres fraco
+ 1 vCPU + latência) essa query 2× por iteração é o que satura em ~23 req/s / p95 7 s.

Otimizações, por ordem de alavanca:
1. **Cachear a version do `ContentVersionProvider`** (TTL curto, ou invalidar no evento de
   import) — elimina as ~129 mil queries; o 304 passa a ser praticamente de graça.
2. **Eliminar o `DISCARD ALL`** (21 % do DB): `No Reset On Close=true` na connection string do
   Npgsql.
3. GC (6,4 %) cai sozinho ao cortar a alocação por request; thread-pool/async **não** precisam
   de ajuste.

## 7. Escopo

Não entra em CI/CD — ferramenta sob demanda, como o restante de `tests/load/` (ver seção 8 do
[`README.md`](README.md)). `docker-compose.profiling.yml` é um overlay adicional, separado do
`docker-compose.load.yml` (Prometheus remote-write); podem ser combinados se for útil ver os
painéis do k6 no Grafana durante o profiling.
