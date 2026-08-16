#!/bin/bash
set -euo pipefail

# run-footprint.sh — mede consumo REAL de recursos por container lendo cgroup v2
# direto (/sys/fs/cgroup), pensado para produção com outras aplicações de pé.
#
# Diferença deliberada para tests/load/profiling/run-profile.sh: aquele script é
# destrutivo (zera pg_stat_statements) e serve para isolar gargalo sob carga local.
# Este aqui NUNCA escreve, reinicia, reseta ou faz prune de nada — só leitura de
# /sys/fs/cgroup, docker ps/inspect/stats (somente leitura) e /proc, /sys do host.

usage() {
  cat <<'EOF'
Uso:
  run-footprint.sh <janela> [duracao_s] [intervalo_s] [rotulo_carga]
  run-footprint.sh summarize <arquivo.csv>
  run-footprint.sh -h|--help

Janelas (duração/intervalo default, sobrescrevíveis por argumento posicional
ou por variável de ambiente):
  cold     5 min  @ 5s   — pico de RSS é no startup, não em regime.
  idle     60 min @ 60s  — Prometheus/Loki compactam a cada ~10 min; janela
                           curta subestima o orçamento em repouso.
  load     15 min @ 5s   — SÓ MEDE, não dispara carga nenhuma. Quem dispara é
                           você, em paralelo, com tests/load/api/*.js ou
                           tests/load/site/circuits.js. Use o 4º argumento (ou
                           FOOTPRINT_LOAD_LABEL) para rotular qual carga estava
                           no ar — vai para o CSV e para o cabeçalho da tabela.
  deploy   até 30 min @ 5s — SÓ pára antes do teto depois de observar uma
                           RECRIAÇÃO DE CONTAINER de verdade (ID diferente do
                           snapshot inicial — a única prova de que algo foi
                           recriado; container ausente/não-healthy ou build
                           de pé só provam "algo está acontecendo", não
                           bastam sozinhos), seguida de N amostras
                           CONSECUTIVAS já pós-recriação em que a stack está
                           estável e sem build ativo (N configurável). Um
                           deploy típico COMEÇA estável (stack de pé, build
                           ainda não iniciou) — período calmo ANTES da
                           recriação não conta nada para as N amostras, senão
                           a janela fechava assim que a recriação fosse vista
                           pela primeira vez (0 amostras depois dela) ou, se o
                           build sozinho já tivesse "acordado" o critério,
                           fechava logo após o build acabar — ainda ANTES do
                           `docker compose up -d` trocar algum container.
                           O resumo distingue, só pelo texto do critério de
                           parada: recriação observada ou não, e processo de
                           build observado ou não (são fatos independentes —
                           cache hit total ainda recria containers; um build
                           pode rodar e não produzir imagem nova nenhuma).

Exemplos:
  run-footprint.sh cold
  run-footprint.sh idle 300 10
  run-footprint.sh load 900 5 "k6 titulos 24vus"
  run-footprint.sh deploy
  run-footprint.sh summarize /var/tmp/footprint/idle-20260809-120000.csv

Saída:
  stdout        → SÓ o resumo: tabela markdown (p50/p95/máximo, nunca média),
                  pronta para colar em docs/load/footprint.md.
  arquivo CSV   → amostras cruas, uma linha por container/host por amostra.
                  Fica em disco na VPS (default /var/tmp/footprint), NUNCA
                  dentro do repo (o deploy faz git reset --hard e apagaria).

Variáveis de ambiente (todas opcionais, com default sensato):
  FOOTPRINT_DURATION_S           sobrescreve a duração (ou teto, na janela deploy)
  FOOTPRINT_INTERVAL_S           sobrescreve o intervalo entre amostras
  FOOTPRINT_LOAD_LABEL           rótulo da carga (janela load)
  FOOTPRINT_OUT_DIR              default /var/tmp/footprint
  FOOTPRINT_NETIO_INTERVAL_S     cadência alvo p/ `docker stats` (NetIO/BlockIO),
                                 default 30s — ele é lento (~2 min medidos nesta
                                 VPS com ~8 containers), por isso roda mais raro
                                 que as demais métricas, nunca a cada amostra.
  FOOTPRINT_APP_CONTAINER        nome do container do app, default tesouro-direto-app
  FOOTPRINT_SONAR_CONTAINER      nome do container do Sonar, default tesouro-direto-sonar
  FOOTPRINT_DEPLOY_STABLE_SAMPLES  nº de amostras CONSECUTIVAS pós-recriação
                                    (estável + sem build ativo) p/ encerrar a
                                    janela deploy antes do teto, default 6.
                                    Amostras ANTES da recriação de container
                                    não contam nada para essa contagem — ver
                                    janela `deploy` acima.
  FOOTPRINT_DOCKER_TIMEOUT_S       teto para chamadas rápidas ao daemon
                                    (docker ps/inspect/compose ps), default 20s.
                                    Usa `timeout` só se existir no PATH (ausente
                                    no macOS); sem `timeout`, roda sem teto.
  FOOTPRINT_STATS_TIMEOUT_S        teto para `docker stats` (mais lento, já
                                    medido em >2min nesta VPS), default 180s.
                                    Mesma degradação sem `timeout`.

Não há env obrigatória sem default — o script só lê, nunca precisa de segredo.
EOF
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ] || [ "${#}" -eq 0 ]; then
  usage
  exit 0
fi

# ---------------------------------------------------------------------------
# Colunas do CSV — schema único para linhas de container e linhas de host
# (campos que não se aplicam a um tipo de linha ficam vazios). Isso evita
# múltiplos arquivos e mantém "amostras cruas → 1 CSV" como pedido.
# ---------------------------------------------------------------------------
COLUMNS=(timestamp janela rotulo_carga tipo nome container_id \
  memory_current memory_peak mem_anon mem_file mem_shmem mem_kernel mem_slab \
  oom_kill cpu_usage_usec cpu_nr_periods cpu_nr_throttled cpu_throttled_usec \
  pids_current net_io_raw block_io_raw \
  host_mem_total_mb host_mem_used_mb host_mem_avail_mb host_swap_used_mb \
  host_load1 host_load5 host_load15 host_disk_used_pct \
  build_rss_kb sonar_status \
  docker_restart_count docker_oom_killed)

declare -A ROW=()

# Última leitura de build_rss_kb (ver build_rss_kb()/emit_host_row) — lida
# pela janela "deploy" para saber se há build ativo nesta amostra (zera
# stable_count e arma o latch BUILD_OBSERVADO, ver run_window), sem repetir
# o `ps` já feito em emit_host_row.
LAST_BUILD_RSS_KB=0

csv_header() {
  local IFS=,
  echo "${COLUMNS[*]}"
}

emit_row() {
  local -a vals=()
  local k
  for k in "${COLUMNS[@]}"; do
    vals+=("${ROW[$k]:-}")
  done
  local IFS=,
  echo "${vals[*]}" >> "$CSV_FILE"
  ROW=()
}

# ---------------------------------------------------------------------------
# cgroup v2 — leitura pura, tolerante a ausência (container pode sumir/trocar
# de ID entre a listagem e a leitura: recriação em andamento, ou apenas parou).
# ---------------------------------------------------------------------------
CGROUP_SCOPE_BASE="/sys/fs/cgroup/system.slice"

cgroup_path_for() {
  local full_id="$1"
  local p="$CGROUP_SCOPE_BASE/docker-${full_id}.scope"
  if [ -d "$p" ]; then
    echo "$p"
    return 0
  fi
  local found
  found=$(find /sys/fs/cgroup -maxdepth 6 -type d -name "docker-${full_id}.scope" 2>/dev/null | head -n1 || true)
  if [ -n "$found" ]; then
    echo "$found"
    return 0
  fi
  return 1
}

read_kv() {
  # lê "chave valor" de um arquivo tipo memory.stat/cpu.stat/memory.events;
  # 0 se a chave não existir (ex.: "kernel" ausente em kernels mais antigos)
  # ou se o arquivo já não existir mais (container recriado/parado na hora H).
  local file="$1" key="$2"
  awk -v k="$key" '$1==k{print $2; f=1} END{if(!f) print 0}' "$file" 2>/dev/null || echo 0
}

# ---------------------------------------------------------------------------
# Teto para chamadas ao daemon Docker — se ele travar (mais provável durante
# um deploy, que é exatamente a janela mais longa/crítica), o script não pode
# bloquear indefinidamente. `timeout` é Linux-only (ausente no macOS, já
# quebrou script deste repo antes) — usa se existir, degrada para sem-teto
# se não existir, NUNCA dependência dura.
# ---------------------------------------------------------------------------
HAVE_TIMEOUT=1
command -v timeout >/dev/null 2>&1 || HAVE_TIMEOUT=0

run_with_timeout() {
  local secs="$1"
  shift
  if [ "$HAVE_TIMEOUT" -eq 1 ]; then
    timeout "$secs" "$@"
  else
    "$@"
  fi
}

# ---------------------------------------------------------------------------
# Containers vivos — re-resolvido A CADA AMOSTRA (não uma vez no início): o ID
# muda quando um container é recriado (é exatamente o que acontece na janela
# "deploy"), e resolver só no início faria a leitura de cgroup apontar para um
# caminho morto no resto da janela.
# ---------------------------------------------------------------------------
CUR_NAMES=()
CUR_IDS=()

refresh_containers() {
  CUR_NAMES=()
  CUR_IDS=()
  local name id
  while IFS=$'\t' read -r name id; do
    [ -n "$name" ] || continue
    CUR_NAMES+=("$name")
    CUR_IDS+=("$id")
  done < <(run_with_timeout "$DOCKER_TIMEOUT_S" docker ps --no-trunc --format '{{.Names}}\t{{.ID}}' 2>/dev/null || true)
}

# ---------------------------------------------------------------------------
# Verdade do Docker (não zera em restart-por-política): RestartCount é
# cumulativo e sobrevive ao restart do mesmo container_id — só zera se o
# container for de fato recriado, e aí o ID muda e o segmento de cgroup já
# cobre o caso. OOMKilled reflete o estado do último start. Um ÚNICO
# `docker inspect` com todos os IDs atuais (mesmo custo de ANTES, não some 1
# chamada por container) — não confundir com o `docker ps` de refresh_containers,
# que só resolve nome→ID e não carrega esses dois campos.
# ---------------------------------------------------------------------------
declare -A RESTART_MAP=()
declare -A OOMKILLED_MAP=()

collect_docker_meta() {
  RESTART_MAP=()
  OOMKILLED_MAP=()
  [ "${#CUR_IDS[@]}" -eq 0 ] && return 0
  local out id rc oomk
  out=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker inspect \
    --format '{{.Id}}|{{.RestartCount}}|{{.State.OOMKilled}}' \
    "${CUR_IDS[@]}" 2>/dev/null || true)
  while IFS='|' read -r id rc oomk; do
    [ -n "$id" ] || continue
    RESTART_MAP["$id"]="$rc"
    OOMKILLED_MAP["$id"]="$oomk"
  done <<< "$out"
}

sample_container() {
  local ts="$1" name="$2" full_id="$3" netio="$4" blockio="$5"
  local cpath
  if ! cpath=$(cgroup_path_for "$full_id"); then
    return 0
  fi
  [ -r "$cpath/memory.current" ] || return 0

  ROW=()
  ROW[timestamp]="$ts"
  ROW[janela]="$WINDOW"
  ROW[rotulo_carga]="$LOAD_LABEL"
  ROW[tipo]="container"
  ROW[nome]="$name"
  ROW[container_id]="$full_id"
  ROW[memory_current]="$(cat "$cpath/memory.current" 2>/dev/null || echo 0)"
  ROW[memory_peak]="$(cat "$cpath/memory.peak" 2>/dev/null || echo 0)"
  ROW[mem_anon]="$(read_kv "$cpath/memory.stat" anon)"
  ROW[mem_file]="$(read_kv "$cpath/memory.stat" file)"
  ROW[mem_shmem]="$(read_kv "$cpath/memory.stat" shmem)"
  ROW[mem_kernel]="$(read_kv "$cpath/memory.stat" kernel)"
  ROW[mem_slab]="$(read_kv "$cpath/memory.stat" slab)"
  ROW[oom_kill]="$(read_kv "$cpath/memory.events" oom_kill)"
  ROW[cpu_usage_usec]="$(read_kv "$cpath/cpu.stat" usage_usec)"
  ROW[cpu_nr_periods]="$(read_kv "$cpath/cpu.stat" nr_periods)"
  ROW[cpu_nr_throttled]="$(read_kv "$cpath/cpu.stat" nr_throttled)"
  ROW[cpu_throttled_usec]="$(read_kv "$cpath/cpu.stat" throttled_usec)"
  ROW[pids_current]="$(cat "$cpath/pids.current" 2>/dev/null || echo 0)"
  ROW[net_io_raw]="$netio"
  ROW[block_io_raw]="$blockio"
  ROW[docker_restart_count]="${RESTART_MAP[$full_id]:-}"
  ROW[docker_oom_killed]="${OOMKILLED_MAP[$full_id]:-}"
  emit_row
}

# ---------------------------------------------------------------------------
# docker stats — só para NetIO/BlockIO (sem equivalente simples em cgroup v2).
# Chamado no máximo 1x por amostra, e mais raro que o resto: nesta VPS uma
# chamada --no-stream levou >2min com ~8 containers. Cadência própria, para
# não distorcer o intervalo das métricas de cgroup (rápidas).
# ---------------------------------------------------------------------------
declare -A NETIO_MAP=()
declare -A BLOCKIO_MAP=()

collect_docker_stats() {
  NETIO_MAP=()
  BLOCKIO_MAP=()
  local name netio blockio
  while IFS=$'\t' read -r name netio blockio; do
    [ -n "$name" ] || continue
    NETIO_MAP["$name"]="$netio"
    BLOCKIO_MAP["$name"]="$blockio"
  done < <(run_with_timeout "$STATS_TIMEOUT_S" docker stats --no-stream --format '{{.Name}}\t{{.NetIO}}\t{{.BlockIO}}' 2>/dev/null || true)
}

# ---------------------------------------------------------------------------
# Host: memória, swap, load average, disco de /.
# ---------------------------------------------------------------------------
collect_host_snapshot() {
  local total used avail swap l1 l5 l15 disk
  read -r total used avail <<< "$(free -m | awk '/^Mem:/{print $2, $3, $7}')"
  swap=$(free -m | awk '/^Swap:/{print $3}')
  if [ -r /proc/loadavg ]; then
    read -r l1 l5 l15 _ < /proc/loadavg
  else
    l1="n/d"; l5="n/d"; l15="n/d"
  fi
  disk=$(df -P / | awk 'NR==2{gsub("%","",$5); print $5}')
  printf '%s|%s|%s|%s|%s|%s|%s|%s\n' "$total" "$used" "$avail" "$swap" "$l1" "$l5" "$l15" "$disk"
}

# Pico de build: soma de RSS dos processos de build — não é governado por
# nenhum limite de compose (roda no BuildKit do daemon, fora do cgroup do
# serviço), e já foi medido em 610 MB de pico nesta VPS.
#
# ARMADILHA #1 (achada testando ao vivo): `comm` sozinho ("dotnet") não
# distingue uma invocação de build ("dotnet build/publish/restore") do
# processo do app RODANDO ("dotnet TesouroDireto.API.dll") — os dois têm o
# mesmo `comm`, e o 2º aparece no `ps` do HOST mesmo estando dentro de um
# container (a menos que o container use pid namespace próprio isolado do
# host, o que não é o caso aqui). Por isso o match é na linha de comando
# inteira, nunca em `comm`.
#
# ARMADILHA #2 (achada ao vivo na VPS, mesma família da já registrada neste
# projeto sobre `pgrep -f` enxergar o próprio comando que o executa — aqui
# dentro do `awk`): `ps -eo rss,args` lista o PRÓPRIO processo `awk` abaixo,
# e `args` desse processo é o TEXTO INTEIRO deste programa — que contém os
# literais "MSBuild" e "VBCSCompiler". As regras de substring nua casavam
# com o awk que as executa, e casavam as DUAS ao mesmo tempo, dobrando o RSS
# do próprio `awk` num "pico de build" fantasma (~8 MB com build nenhum
# rodando). A defesa NÃO é um truque de classe de caractere tipo
# "MSBuil[d]" — invisível pra quem editar depois e volta a quebrar no
# primeiro "clean-up" do regex. É um sentinela literal, óbvio, que só existe
# NESTE programa: como o awk carrega o próprio texto do programa em `args`,
# a linha do awk se auto-exclui por construção, e a razão fica legível no
# código. `next` depois de cada match, além de aplicar o guard, também evita
# contar o MESMO processo real 2x: uma linha de build de verdade tipo
# `dotnet build /src/MSBuildTools/Foo.csproj` casaria a regra do `dotnet` E
# a regra nua de "MSBuild" (basta o CAMINHO do projeto conter a substring) —
# sem `next`, um único processo real entraria em dobro na soma.
build_rss_kb() {
  ps -eo rss,args 2>/dev/null | awk '
    {
      rss = $1
      sub(/^[^ \t]+[ \t]+/, "")
      line = $0
    }
    line ~ /FOOTPRINT_BUILD_RSS_SELFMATCH_GUARD_DO_NOT_REMOVE/ { next }
    line ~ /(^|\/)dotnet[ \t]+(build|publish|restore)([ \t]|$)/ { sum += rss; next }
    line ~ /MSBuild/ { sum += rss; next }
    line ~ /VBCSCompiler/ { sum += rss; next }
    END { print sum+0 }'
}

sonar_status() {
  local st
  st=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker ps -a --filter "name=^/${SONAR_CONTAINER}\$" --format '{{.State}}' 2>/dev/null || true)
  if [ -z "$st" ]; then
    echo "ausente"
  else
    echo "$st"
  fi
}

emit_host_row() {
  local ts="$1"
  local total used avail swap l1 l5 l15 disk
  IFS='|' read -r total used avail swap l1 l5 l15 disk <<< "$(collect_host_snapshot)"
  ROW=()
  ROW[timestamp]="$ts"
  ROW[janela]="$WINDOW"
  ROW[rotulo_carga]="$LOAD_LABEL"
  ROW[tipo]="host"
  ROW[nome]="host"
  ROW[host_mem_total_mb]="$total"
  ROW[host_mem_used_mb]="$used"
  ROW[host_mem_avail_mb]="$avail"
  ROW[host_swap_used_mb]="$swap"
  ROW[host_load1]="$l1"
  ROW[host_load5]="$l5"
  ROW[host_load15]="$l15"
  ROW[host_disk_used_pct]="$disk"
  ROW[build_rss_kb]="$(build_rss_kb)"
  LAST_BUILD_RSS_KB="${ROW[build_rss_kb]}"
  ROW[sonar_status]="$(sonar_status)"
  emit_row
}

# ---------------------------------------------------------------------------
# Critério de estabilidade da janela "deploy": containers esperados de pé +
# app healthy por N amostras seguidas. Resolvido 1x no início (lista de
# serviços do compose no diretório corrente); se não der (script rodando fora
# de um checkout com compose), cai para o snapshot inicial de containers.
# ---------------------------------------------------------------------------
EXPECTED_CONTAINERS=()

resolve_expected_containers() {
  local out
  out=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker compose ps --format '{{.Name}}' 2>/dev/null || true)
  if [ -n "$out" ]; then
    mapfile -t EXPECTED_CONTAINERS <<< "$out"
  else
    refresh_containers
    EXPECTED_CONTAINERS=("${CUR_NAMES[@]}")
  fi
}

deploy_is_stable() {
  local svc found name
  for svc in "${EXPECTED_CONTAINERS[@]}"; do
    [ -n "$svc" ] || continue
    found=0
    for name in "${CUR_NAMES[@]}"; do
      if [ "$name" = "$svc" ]; then
        found=1
        break
      fi
    done
    [ "$found" -eq 1 ] || return 1
  done
  if [ -n "$APP_CONTAINER" ]; then
    local health
    health=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}sem-healthcheck{{end}}' "$APP_CONTAINER" 2>/dev/null || echo "ausente")
    if [ "$health" != "healthy" ] && [ "$health" != "sem-healthcheck" ]; then
      return 1
    fi
  fi
  return 0
}

# ---------------------------------------------------------------------------
# Recriação de container na janela "deploy" — a ÚNICA prova de que a stack
# de fato se moveu, usada como gatilho do latch RECRIACAO_OBSERVADA (ver
# run_window). `deploy_is_stable` falso e build ativo (build_rss_kb>0) só
# provam "algo está acontecendo agora", não que um container foi recriado —
# um deploy típico é git reset --hard → nginx reload → docker compose build
# (minutos, TODOS os containers de pé e o app healthy, ou seja
# deploy_is_stable() já dá verdadeiro) → só então docker compose up -d. Se
# qualquer um desses dois virasse gatilho do latch, a janela poderia fechar
# logo depois do build acabar — ANTES do `up -d` trocar container nenhum
# (ver comentário em run_window sobre por que o build não conta aqui).
#
# INITIAL_ID_MAP é o snapshot nome→ID capturado 1x, logo após
# resolve_expected_containers, ANTES de qualquer amostra real. Comparar o ID
# atual contra esse snapshot (não contra a amostra anterior) é o único sinal
# que sobrevive a uma recriação rápida entre duas amostras de 5s — serviços
# leves como node-exporter/promtail sobem em bem menos que isso, e sem
# comparar contra o snapshot inicial um `up -d --force-recreate` pode não
# deixar rastro de instabilidade nenhum (container novo já healthy na
# primeira amostra em que é visto).
# ---------------------------------------------------------------------------
declare -A INITIAL_ID_MAP=()
RECRIACAO_OBSERVADA=0
BUILD_OBSERVADO=0
DETECCAO_INICIAL_CEGA=0
DETECCAO_INICIAL_COBERTURA=""

capture_initial_id_map() {
  INITIAL_ID_MAP=()
  local i
  for i in "${!CUR_NAMES[@]}"; do
    INITIAL_ID_MAP["${CUR_NAMES[$i]}"]="${CUR_IDS[$i]}"
  done
}

container_recreated_since_start() {
  local i name id
  for i in "${!CUR_NAMES[@]}"; do
    name="${CUR_NAMES[$i]}"
    id="${CUR_IDS[$i]}"
    if [ -n "${INITIAL_ID_MAP[$name]:-}" ] && [ "${INITIAL_ID_MAP[$name]}" != "$id" ]; then
      return 0
    fi
  done
  return 1
}

# ---------------------------------------------------------------------------
# Cobertura do snapshot inicial — se o daemon do Docker engasgar (ou
# `run_with_timeout` estourar) exatamente no instante em que a janela é
# armada, `capture_initial_id_map` pode terminar vazio ou incompleto.
# `container_recreated_since_start` NUNCA dispara para um nome ausente do
# snapshot — não porque não houve recriação, mas porque o detector nunca
# teve um ID de referência para comparar. Sem checar isso, uma falha do
# daemon vira silenciosamente "nenhuma recriação observada" no relatório
# final: parece um resultado válido, mas é indistinguível de o detector
# nunca ter tido chance de ver nada.
#
# Checado 1x, logo após o armamento (não a cada amostra): grita no stderr NA
# HORA — o operador ainda está olhando o terminal nesse instante — e registra
# o estado em DETECCAO_INICIAL_CEGA/DETECCAO_INICIAL_COBERTURA para
# `deploy_facts_suffix` nunca mais afirmar ausência de recriação sobre um
# detector que pode simplesmente não ter enxergado nada. Cobertura PARCIAL
# (algumas entradas, não todas) recebe o MESMO tratamento do vazio total: o
# detector funciona para os nomes capturados e é cego para o resto, então a
# afirmação "nenhuma recriação" continua não confiável — não há meio-termo
# "parcialmente confiável" aqui, só "confiável" ou "cego".
# ---------------------------------------------------------------------------
check_initial_snapshot_coverage() {
  local svc n_expected=0 n_captured=0
  for svc in "${EXPECTED_CONTAINERS[@]}"; do
    [ -n "$svc" ] || continue
    n_expected=$((n_expected + 1))
    [ -n "${INITIAL_ID_MAP[$svc]:-}" ] && n_captured=$((n_captured + 1))
  done
  DETECCAO_INICIAL_COBERTURA="${n_captured}/${n_expected}"
  if [ "$n_expected" -eq 0 ] || [ "$n_captured" -lt "$n_expected" ]; then
    DETECCAO_INICIAL_CEGA=1
    echo "AVISO: snapshot inicial de IDs de container incompleto (${DETECCAO_INICIAL_COBERTURA} containers esperados capturados) — a detecção de recriação por troca de ID fica CEGA para os containers não capturados nesta janela (docker ps/daemon pode ter engasgado no armamento). O relatório final vai marcar esse trecho como não confiável em vez de afirmar 'nenhuma recriação'." >&2
  fi
}

# ---------------------------------------------------------------------------
# Loop principal de amostragem — laço com relógio de parede, sem depender de
# `timeout` (ausente no macOS; já quebrou um script deste repo antes).
# ---------------------------------------------------------------------------
# Rótulo do critério "prazo estourado", usado nos dois pontos do laço em que
# o prazo pode encerrar a janela (ver run_window). Mesmo rótulo por janela,
# reportando o real x alvo — se estourou, quanto.
deadline_label() {
  [ "$WINDOW" = "deploy" ] && echo "tempo_maximo" || echo "duracao_alvo"
}

format_deadline_reason() {
  local label="$1" target="$2" real="$3" overshoot
  overshoot=$(( real - target ))
  if [ "$overshoot" -gt 0 ]; then
    printf '%s (%ss); real %ss (estouro %ss)' "$label" "$target" "$real" "$overshoot"
  else
    printf '%s (%ss); real %ss' "$label" "$target" "$real"
  fi
}

# Sufixo SEMPRE anexado ao STOP_REASON da janela "deploy" — em QUALQUER dos
# dois caminhos de parada (estabilidade ou prazo), não só no prazo. Relata os
# dois fatos independentes que a janela observou (recriação de container?
# processo de build?), porque um só não implica o outro: cache hit total no
# `docker compose build` ainda pode recriar container (compose muda hash de
# config sem rebuildar imagem); e um build pode rodar minutos e não produzir
# imagem nova nenhuma (cache hit real), sem trocar container. Com os dois
# fatos sempre presentes, os casos ficam distinguíveis lendo só o
# STOP_REASON, sem inferir nada do resto do texto:
#   recriação=sim, build=sim  → deploy normal, mudou código/imagem
#   recriação=sim, build=não  → troca de container sem rebuild (ex.: só config)
#   recriação=não, build=sim  → build rodou e não produziu troca de container
#   recriação=não, build=não  → deploy no-op (nada mudou)
#   detecção cega (snapshot inicial vazio/incompleto) → NUNCA "nenhuma
#   recriação": a ausência relatada seria indistinguível de o detector nunca
#   ter tido chance de ver nada (ver check_initial_snapshot_coverage). Essa
#   checagem vale mesmo se RECRIACAO_OBSERVADA==1 — uma recriação REAL foi
#   vista para um nome capturado, então essa parte do fato continua valendo;
#   o que é untrustworthy é só a AUSÊNCIA relatada para os nomes fora do
#   snapshot, por isso a nota de cegueira é anexada como alerta adicional
#   nesse caso, sem apagar o "sim" já confirmado.
deploy_facts_suffix() {
  local build_txt="não"
  [ "$BUILD_OBSERVADO" -eq 1 ] && build_txt="sim"

  if [ "$RECRIACAO_OBSERVADA" -eq 1 ]; then
    printf '; recriação de container observada na janela (ID diferente do snapshot inicial); processo de build observado: %s' "$build_txt"
    if [ "$DETECCAO_INICIAL_CEGA" -eq 1 ]; then
      printf '; ATENÇÃO: snapshot inicial de IDs incompleto (%s containers esperados capturados) — pode haver recriações adicionais entre os containers fora da cobertura que este relatório não detectou' "$DETECCAO_INICIAL_COBERTURA"
    fi
    return
  fi

  if [ "$DETECCAO_INICIAL_CEGA" -eq 1 ]; then
    printf '; DETECÇÃO DE RECRIAÇÃO CEGA nesta janela (snapshot inicial de IDs capturou só %s dos containers esperados): a ausência de recriação relatada aqui é INDISTINGUÍVEL de falha do próprio detector — NÃO é evidência de que nenhum container foi recriado, é ausência de dado' "$DETECCAO_INICIAL_COBERTURA"
    return
  fi

  if [ "$BUILD_OBSERVADO" -eq 1 ]; then
    printf '; NENHUMA recriação de container observada na janela, apesar de processo de build detectado: o build rodou e não produziu troca de container (cache hit / imagem igual)'
  else
    printf '; nenhum churn observado na janela: nenhum container recriado e nenhum processo de build detectado'
  fi
}

run_window() {
  local start_ts now elapsed sample_idx=0 stable_count=0
  start_ts=$(date +%s)
  STOP_REASON=""
  RECRIACAO_OBSERVADA=0
  BUILD_OBSERVADO=0
  DETECCAO_INICIAL_CEGA=0
  DETECCAO_INICIAL_COBERTURA=""

  if [ "$WINDOW" = "deploy" ]; then
    resolve_expected_containers
    refresh_containers
    capture_initial_id_map
    check_initial_snapshot_coverage
  fi

  while :; do
    now=$(date +%s)
    elapsed=$(( now - start_ts ))

    if [ "$elapsed" -ge "$DURATION_S" ]; then
      STOP_REASON=$(format_deadline_reason "$(deadline_label)" "$DURATION_S" "$elapsed")
      break
    fi

    local do_netio=0
    if (( sample_idx % NETIO_EVERY == 0 )); then
      do_netio=1
      collect_docker_stats
    fi

    refresh_containers
    collect_docker_meta
    local ts
    ts=$(date +%s)

    local i name fid netio blockio
    for i in "${!CUR_NAMES[@]}"; do
      name="${CUR_NAMES[$i]}"
      fid="${CUR_IDS[$i]}"
      netio=""
      blockio=""
      if [ "$do_netio" -eq 1 ]; then
        netio="${NETIO_MAP[$name]:-}"
        blockio="${BLOCKIO_MAP[$name]:-}"
      fi
      sample_container "$ts" "$name" "$fid" "$netio" "$blockio"
    done

    emit_host_row "$ts"

    if [ "$WINDOW" = "deploy" ]; then
      # Estado desta amostra: is_stable (container_recreated_since_start
      # continua vivo? deploy_is_stable) e build_ativo (build_rss_kb>0 nesta
      # amostra de host).
      local is_stable=1 build_ativo=0
      deploy_is_stable || is_stable=0
      [ "${LAST_BUILD_RSS_KB:-0}" -gt 0 ] && build_ativo=1
      [ "$build_ativo" -eq 1 ] && BUILD_OBSERVADO=1

      # RECRIACAO_OBSERVADA é um latch (nunca desarma) e o ÚNICO gatilho é
      # container_recreated_since_start — ID diferente do snapshot inicial é
      # a ÚNICA prova de que um container foi de fato recriado.
      # `is_stable`/`build_ativo` NÃO armam esse latch: eles só provam "algo
      # está acontecendo agora", e usá-los como gatilho tem um defeito
      # concreto e medido — se o build sozinho armasse o latch, bastariam
      # STABLE_SAMPLES amostras calmas com os containers ANTIGOS ainda de pé
      # (o intervalo normal entre "build acabou" e "up -d trocou os
      # containers") para a janela fechar ANTES de qualquer recriação real.
      if container_recreated_since_start; then
        RECRIACAO_OBSERVADA=1
      fi

      # stable_count só incrementa quando as TRÊS condições valem ao mesmo
      # tempo: (1) já houve recriação (RECRIACAO_OBSERVADA), (2) a stack está
      # estável AGORA, (3) não há build ativo AGORA. Qualquer amostra fora
      # dessas três condições zera — inclusive as calmas ANTES da recriação
      # (RECRIACAO_OBSERVADA ainda 0 nelas), que portanto NUNCA contribuem
      # para as N amostras exigidas. Isso resolve por construção, sem
      # depender de detectar a TRANSIÇÃO calma→recriação: o contador só
      # existe, literalmente, como "amostras consecutivas pós-recriação", que
      # é exatamente o que STOP_REASON abaixo afirma.
      if [ "$RECRIACAO_OBSERVADA" -eq 1 ] && [ "$is_stable" -eq 1 ] && [ "$build_ativo" -eq 0 ]; then
        stable_count=$((stable_count + 1))
      else
        stable_count=0
      fi

      if [ "$stable_count" -ge "$STABLE_SAMPLES" ]; then
        STOP_REASON="estabilidade (${STABLE_SAMPLES} amostras consecutivas pós-recriação, sem build ativo)"
        break
      fi
    fi

    sample_idx=$((sample_idx + 1))

    # Prazo checado de novo AQUI — depois da amostra (que pode levar minutos:
    # `docker stats` já mediu >2min nesta VPS) e ANTES de dormir. Sem isso, o
    # laço só via o estouro no topo da PRÓXIMA iteração e ainda rodava um
    # ciclo inteiro (amostra + sono) além do prazo — medido ~30% de estouro
    # numa janela curta. Se o prazo já passou, não dorme: encerra agora.
    local after elapsed2
    after=$(date +%s)
    elapsed2=$(( after - start_ts ))
    if [ "$elapsed2" -ge "$DURATION_S" ]; then
      STOP_REASON=$(format_deadline_reason "$(deadline_label)" "$DURATION_S" "$elapsed2")
      break
    fi

    local spent remaining deadline_left
    spent=$(( after - now ))
    remaining=$(( INTERVAL_S - spent ))
    deadline_left=$(( DURATION_S - elapsed2 ))
    if [ "$remaining" -gt "$deadline_left" ]; then
      remaining=$deadline_left
    fi
    if [ "$remaining" -gt 0 ]; then
      sleep "$remaining"
    fi
  done

  # Anexado UMA vez, depois do laço, independente de qual dos três `break`
  # acima disparou — cobre inclusive o crashloop (recriação vista, mas nunca
  # fica healthy: RECRIACAO_OBSERVADA=1, is_stable sempre falso, janela vai
  # até o teto) sem precisar duplicar a chamada nos três pontos de saída.
  if [ "$WINDOW" = "deploy" ]; then
    STOP_REASON="${STOP_REASON}$(deploy_facts_suffix)"
  fi
}

# ---------------------------------------------------------------------------
# summarize — recalcula a tabela a partir de um CSV já coletado (ao vivo ou
# antigo). É a MESMA função usada ao final de uma coleta ao vivo.
# ---------------------------------------------------------------------------

percentiles_from_file() {
  # imprime "p50<TAB>p95<TAB>max"; "n/d" nos três se o arquivo estiver vazio
  # ou nem existir (ex.: nr_periods sempre 0 -> sem CPU limitada -> throttling
  # é indefinido, não dividir por zero e não poluir a tabela com isso).
  local f="$1" n
  if [ ! -f "$f" ]; then
    printf 'n/d\tn/d\tn/d\n'
    return
  fi
  n=$(wc -l < "$f" 2>/dev/null | tr -d ' ')
  n=${n:-0}
  if [ "$n" -eq 0 ]; then
    printf 'n/d\tn/d\tn/d\n'
    return
  fi
  local sorted="${f}.sorted"
  sort -n "$f" > "$sorted"
  local p50_idx p95_idx
  p50_idx=$(( (n * 50 + 99) / 100 ))
  p95_idx=$(( (n * 95 + 99) / 100 ))
  [ "$p50_idx" -lt 1 ] && p50_idx=1
  [ "$p95_idx" -gt "$n" ] && p95_idx=$n
  local p50 p95 mx
  p50=$(awk -v i="$p50_idx" 'NR==i{print; exit}' "$sorted")
  p95=$(awk -v i="$p95_idx" 'NR==i{print; exit}' "$sorted")
  mx=$(tail -n1 "$sorted")
  rm -f "$sorted"
  printf '%s\t%s\t%s\n' "$p50" "$p95" "$mx"
}

mb() {
  local v="$1"
  if [ "$v" = "n/d" ] || [ -z "$v" ]; then
    echo "n/d"
  else
    awk -v v="$v" 'BEGIN{printf "%.1f", v/1048576}'
  fi
}

round1() {
  local v="$1"
  if [ "$v" = "n/d" ] || [ -z "$v" ]; then
    echo "n/d"
  else
    awk -v v="$v" 'BEGIN{printf "%.1f", v}'
  fi
}

stats_col() {
  # extrai a coluna $2 (1-indexada) das linhas de container do $nome, calcula
  # p50/p95/máx; $3 = "mb" converte bytes -> MB, "int" arredonda p/ inteiro,
  # qualquer outro valor mantém como está.
  local nome="$1" col="$2" conv="$3"
  local tmp
  tmp="$TMP_DIR/col_${col}_$RANDOM.txt"
  awk -F',' -v nome="$nome" -v c="$col" '$4=="container" && $5==nome && $c!="" {print $c}' "$CSV_FILE" > "$tmp"
  local line p50 p95 mx
  line=$(percentiles_from_file "$tmp")
  IFS=$'\t' read -r p50 p95 mx <<< "$line"
  rm -f "$tmp"
  if [ "$p50" = "n/d" ]; then
    echo "n/d"
    return
  fi
  case "$conv" in
    mb) printf '%s/%s/%s' "$(mb "$p50")" "$(mb "$p95")" "$(mb "$mx")" ;;
    int) printf '%s/%s/%s' "$(awk -v v="$p50" 'BEGIN{printf "%.0f", v}')" \
                            "$(awk -v v="$p95" 'BEGIN{printf "%.0f", v}')" \
                            "$(awk -v v="$mx" 'BEGIN{printf "%.0f", v}')" ;;
    *) printf '%s/%s/%s' "$(round1 "$p50")" "$(round1 "$p95")" "$(round1 "$mx")" ;;
  esac
}

stats_nao_descartavel() {
  # coluna derivada anon+shmem+kernel — a que mais se aproxima do que decide OOM
  # (memory.current sozinho superestima por contar page cache descartável; anon
  # sozinho subestima por ignorar shmem, que só sai via swap).
  #
  # NÃO somar `slab` aqui: no cgroup v2 `slab` está DENTRO de `kernel`, e somar
  # os dois conta slab duas vezes. Verificado nesta VPS no container `app`:
  # kernel=3002368 e slab+kernel_stack+pagetables+percpu+sock+vmalloc=2988596
  # (a diferença de ~13 KB são alocações kernel menores não detalhadas), com
  # slab=1893896 = slab_reclaimable+slab_unreclaimable. `slab` continua na
  # tabela como coluna própria, por ser útil de ver isolada.
  #
  # DIVERGÊNCIA PROPOSITAL (revisão adversarial da 80.2, 2026-08-16): desde
  # essa fase, `infra/host/container-metrics.sh` usa
  # anon+shmem+(kernel-slab_reclaimable) em produção — mais preciso, pois
  # slab_reclaimable volta ao sistema sob pressão sem OOM. Esta função NÃO
  # acompanha a mudança: fica em anon+shmem+kernel de propósito, para manter
  # comparabilidade com todas as medições históricas já registradas no PLANO
  # (74.x, 76, 77.x), feitas com essa fórmula. anon+shmem+kernel deixou de
  # ser "a fórmula canônica do projeto" na 80.2 — é a fórmula histórica deste
  # script de profiling. Fonte de verdade para produção:
  # infra/host/container-metrics.sh.
  local nome="$1"
  local tmp
  tmp="$TMP_DIR/nd_$RANDOM.txt"
  awk -F',' -v nome="$nome" '$4=="container" && $5==nome && $9!="" {print ($9+$11+$12)}' "$CSV_FILE" > "$tmp"
  local line p50 p95 mx
  line=$(percentiles_from_file "$tmp")
  IFS=$'\t' read -r p50 p95 mx <<< "$line"
  rm -f "$tmp"
  if [ "$p50" = "n/d" ]; then
    echo "n/d"
    return
  fi
  printf '%s/%s/%s' "$(mb "$p50")" "$(mb "$p95")" "$(mb "$mx")"
}

peak_overall() {
  # memory.peak zera quando o cgroup é recriado — E ISSO ACONTECE MESMO SEM
  # troca de container_id: um restart por política (restart: unless-stopped,
  # usado em todos os serviços deste projeto) preserva o ID do container mas
  # destrói e recria o cgroup por baixo, zerando memory.peak/memory.events/
  # cpu.stat. Detectar só a troca de ID é CEGA para esse caso — o mais comum
  # de todos. O sinal de "cgroup novo" é qualquer QUEDA num contador
  # cumulativo (oom_kill, usage_usec, nr_periods) entre amostras consecutivas
  # do MESMO container_id; tratamos isso como início de um trecho novo, igual
  # a uma troca de ID. O "pico" relatado é o máximo POR TRECHO (id+reset), não
  # da janela inteira tratada como contínua.
  local nome="$1"
  awk -F',' -v nome="$nome" '
    $4=="container" && $5==nome {
      id=$6; val=$8+0; oom=$14+0; usage=$15+0; periods=$16+0
      newseg=0
      if (!started) newseg=1
      else if (id != prev_id) newseg=1
      else if (oom < prev_oom || usage < prev_usage || periods < prev_periods) newseg=1
      if (newseg) { seg++; segkey = id "#" seg }
      if (val > maxbyseg[segkey]) maxbyseg[segkey]=val
      started=1
      prev_id=id; prev_oom=oom; prev_usage=usage; prev_periods=periods
    }
    END{
      m=0
      for (k in maxbyseg) if (maxbyseg[k] > m) m = maxbyseg[k]
      print m
    }' "$CSV_FILE"
}

count_segments() {
  # nº de trechos = nº de container_id distintos, EM ORDEM cronológica (uniq,
  # não sort -u): cada troca de ID no meio da janela é uma recriação real.
  local nome="$1" c
  c=$(awk -F',' -v nome="$nome" '$4=="container" && $5==nome {print $6}' "$CSV_FILE" | uniq | wc -l | tr -d ' ')
  echo "${c:-0}"
}

oom_total() {
  # oom_kill de memory.events é cumulativo POR CGROUP, e o cgroup reseta em
  # restart-por-política sem trocar o container_id (ver peak_overall). Somar
  # last-first sobre a janela inteira, tratando o ID como contínuo, ESCONDE
  # OOM kills reais: uma sequência 0,0,1,0,0 no mesmo ID (1 kill seguido de
  # restart, que zera o contador) daria last(0)-first(0)=0 — "0 OOM kills"
  # quando na verdade houve 1. Segmenta como peak_overall (id + queda de
  # contador cumulativo = trecho novo) e soma o incremento POR TRECHO —
  # nunca last-first da janela inteira.
  local nome="$1"
  awk -F',' -v nome="$nome" '
    $4=="container" && $5==nome {
      id=$6; oom=$14+0; usage=$15+0; periods=$16+0
      newseg=0
      if (!started) newseg=1
      else if (id != prev_id) newseg=1
      else if (oom < prev_oom || usage < prev_usage || periods < prev_periods) newseg=1
      if (newseg) {
        seg++
        segkey = id "#" seg
        first[segkey]=oom
      }
      last[segkey]=oom
      started=1
      prev_id=id; prev_oom=oom; prev_usage=usage; prev_periods=periods
    }
    END{
      s=0
      for (k in first) s += (last[k]-first[k])
      if (s<0) s=0
      print s
    }' "$CSV_FILE"
}

restart_total() {
  # RestartCount do `docker inspect` — ao contrário do oom_kill do cgroup,
  # NÃO zera em restart-por-política (só zera se o container for de fato
  # recriado, e aí o ID muda e cada ID já é um grupo isolado aqui). Por isso
  # aqui basta agrupar por container_id, sem detecção de reset de cgroup.
  local nome="$1"
  awk -F',' -v nome="$nome" '
    $4=="container" && $5==nome && $32!="" {
      id=$6; val=$32+0
      if (!(id in first)) first[id]=val
      last[id]=val
    }
    END{
      s=0
      for (k in first) s += (last[k]-first[k])
      if (s<0) s=0
      print s
    }' "$CSV_FILE"
}

oom_killed_flag() {
  # true se QUALQUER amostra da janela viu State.OOMKilled=true no
  # `docker inspect` — atravessa restarts (ao contrário do oom_kill do
  # cgroup). Se cgroup e Docker discordarem, o Docker tem razão.
  local nome="$1"
  awk -F',' -v nome="$nome" '
    $4=="container" && $5==nome && tolower($33)=="true" { found=1 }
    END{ print (found ? "SIM" : "não") }' "$CSV_FILE"
}

last_nonempty() {
  local nome="$1" col="$2"
  awk -F',' -v nome="$nome" -v c="$col" '$4=="container" && $5==nome && $c!="" {v=$c} END{print v}' "$CSV_FILE"
}

cpu_and_throttle() {
  # usage_usec/nr_periods/nr_throttled/throttled_usec são CUMULATIVOS desde a
  # criação do cgroup — %CPU e throttling são DELTA entre amostras consecutivas
  # dividido pelo intervalo REAL (não o nominal: docker stats pode atrasar uma
  # amostra, e o delta de timestamp real absorve isso). Delta só é computado
  # dentro do MESMO container_id E do MESMO trecho de cgroup: além da troca de
  # ID, um restart-por-política preserva o ID mas recria o cgroup e zera esses
  # contadores (ver peak_overall/oom_total) — qualquer QUEDA em usage/periods/
  # nr_throttled entre amostras consecutivas do mesmo ID é esse reset. Nesse
  # caso o delta é DESCARTADO (não clampado a zero): um "0%" fabricado nesse
  # ponto de transição poluiria os percentis com uma amostra que não existiu.
  #
  # Escreve as amostras de %CPU (uma por linha) em $cpu_out via redirect
  # DENTRO do awk (não por pipe) — assim o arquivo sempre existe mesmo sem
  # nenhum delta válido, e não há necessidade de dois processos lendo o mesmo
  # stdout sequencialmente (armadilha: o segundo esvaziaria o primeiro).
  # Imprime em stdout "sum_nr_periods sum_nr_throttled sum_throttled_usec".
  local nome="$1" cpu_out="$2"
  awk -F',' -v nome="$nome" -v outfile="$cpu_out" '
    BEGIN{ started=0 }
    $4=="container" && $5==nome {
      ts=$1; id=$6; usage=$15+0; periods=$16+0; nthr=$17+0; thr_usec=$18+0
      reset=0
      if (started && id == prev_id) {
        if (usage < prev_usage || periods < prev_periods || nthr < prev_nthr) reset=1
      }
      if (started && id == prev_id && !reset) {
        dt = ts - prev_ts
        if (dt > 0) {
          dusage = usage - prev_usage
          pct = (dusage / dt) / 10000.0
          print pct > outfile
          dper = periods - prev_periods
          dnthr = nthr - prev_nthr
          dthru = thr_usec - prev_thr_usec
          sum_per += dper
          sum_nthr += dnthr
          sum_thru += dthru
        }
      }
      started=1
      prev_ts=ts; prev_id=id; prev_usage=usage; prev_periods=periods; prev_nthr=nthr; prev_thr_usec=thr_usec
    }
    END{
      close(outfile)
      printf "%d %d %d\n", sum_per+0, sum_nthr+0, sum_thru+0
    }
  ' "$CSV_FILE"
}

cmd_summarize() {
  local csv="$1"
  if [ ! -r "$csv" ]; then
    echo "ERRO: não consegui ler $csv" >&2
    exit 1
  fi
  CSV_FILE="$csv"
  # TMP_DIR + limpeza (trap EXIT) já vêm montados pelo bloco de parsing de
  # argumentos, tanto no modo `summarize` isolado quanto ao final de uma
  # coleta ao vivo — um único lugar cria/limpa, evitando vazar diretório.

  local janela rotulo ts_min ts_max duracao n_container_rows n_host_rows
  janela=$(awk -F',' 'NR==2{print $2; exit}' "$csv")
  rotulo=$(awk -F',' 'NR==2{print $3; exit}' "$csv")
  # min/max de timestamp numa ÚNICA passada de awk, SEM sort/head/tail: um
  # pipe `sort -n | head -1` só escreve depois de ler tudo, `head` lê 1 linha
  # e fecha o pipe, `sort` recebe SIGPIPE (exit 141) e, com `set -o pipefail`,
  # isso derruba o script inteiro EM SILÊNCIO (CSV grande = mais provável).
  local ts_minmax
  ts_minmax=$(awk -F',' 'NR>1 && $1!="" {
      if (mn=="" || $1<mn) mn=$1
      if (mx=="" || $1>mx) mx=$1
    } END{ print mn, mx }' "$csv")
  read -r ts_min ts_max <<< "$ts_minmax"
  if [ -z "${ts_min:-}" ] || [ -z "${ts_max:-}" ]; then
    ts_min=0
    ts_max=0
  fi
  duracao=$(( ts_max - ts_min ))
  n_container_rows=$(awk -F',' '$4=="container"{c++} END{print c+0}' "$csv")
  n_host_rows=$(awk -F',' '$4=="host"{c++} END{print c+0}' "$csv")

  # Cadência de NetIO/BlockIO: medida no timeline de UM único container (não
  # pooling entre todos), senão 8 containers amostrados na mesma rodada
  # inflam a contagem e o cálculo (duração / nº-de-linhas) subestima
  # grosseiramente o intervalo real entre rodadas de `docker stats`.
  local cadencia_netio one_nome netio_ts_file n_ts first_ts last_ts
  one_nome=$(awk -F',' 'NR>1 && $4=="container"{print $5; exit}' "$csv")
  netio_ts_file="$TMP_DIR/netio_ts.txt"
  awk -F',' -v nome="$one_nome" '$4=="container" && $5==nome && $20!="" {print $1}' "$csv" | sort -n > "$netio_ts_file"
  n_ts=$(wc -l < "$netio_ts_file" 2>/dev/null | tr -d ' ')
  n_ts=${n_ts:-0}
  if [ "$n_ts" -gt 1 ]; then
    first_ts=$(head -n1 "$netio_ts_file")
    last_ts=$(tail -n1 "$netio_ts_file")
    cadencia_netio=$(awk -v a="$first_ts" -v b="$last_ts" -v n="$n_ts" 'BEGIN{printf "%.0f", (b-a)/(n-1)}')
  else
    cadencia_netio="n/d (poucas amostras)"
  fi
  rm -f "$netio_ts_file"

  echo "## Footprint — janela \`${janela}\` (rótulo de carga: ${rotulo})"
  echo ""
  echo "Amostras: ${n_host_rows} ciclos, ${n_container_rows} linhas de container."
  echo "Janela real: ${duracao}s (início=$(date -r "$ts_min" 2>/dev/null || date -d "@$ts_min" 2>/dev/null || echo "$ts_min"), fim=$(date -r "$ts_max" 2>/dev/null || date -d "@$ts_max" 2>/dev/null || echo "$ts_max"))."
  echo "Cadência aproximada de NetIO/BlockIO (\`docker stats\`): ~${cadencia_netio}s."
  if [ -n "${STOP_REASON:-}" ]; then
    echo "Critério de parada: ${STOP_REASON}."
  fi
  echo "CSV bruto (fica na VPS, não sobe por aqui): \`${csv}\`"
  echo ""
  echo "### Containers (p50/p95/máximo — NUNCA média)"
  echo ""
  echo "Legenda: \`OOM kills (cgroup)\` só enxerga o trecho de cgroup vivo agora — um restart-por-política (mesmo container ID, cgroup recriado por baixo) zera esse contador; \`Reinícios\` e \`OOMKilled\` vêm do \`docker inspect\` e ATRAVESSAM restarts. Se as duas fontes discordarem, vale a do Docker."
  echo ""
  echo "| Container | Trechos (recriado?) | mem.current MB (p50/p95/máx) | mem.peak máx MB | anon MB (p50/p95/máx) | file MB (p50/p95/máx) | shmem MB (p50/p95/máx) | kernel MB (p50/p95/máx) | slab MB (p50/p95/máx) | não-descartável MB (p50/p95/máx) | OOM kills (cgroup, trecho) | Reinícios (Docker, janela) | OOMKilled (Docker) | CPU% (p50/p95/máx) | throttling (razão) | throttled total (ms) | PIDs (p50/p95/máx) | NetIO última | BlockIO última |"
  echo "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"

  local nomes nome
  nomes=$(awk -F',' 'NR>1 && $4=="container"{print $5}' "$csv" | sort -u)
  for nome in $nomes; do
    local segs recriado
    segs=$(count_segments "$nome")
    if [ "$segs" -gt 1 ]; then
      recriado="${segs} (SIM)"
    else
      recriado="1 (não)"
    fi

    local mem_cur mem_peak_bytes mem_peak anon file_ shmem kernel slab nd oom restarts oomkilled pids net_last block_last
    mem_cur=$(stats_col "$nome" 7 mb)
    mem_peak_bytes=$(peak_overall "$nome")
    mem_peak=$(mb "$mem_peak_bytes")
    anon=$(stats_col "$nome" 9 mb)
    file_=$(stats_col "$nome" 10 mb)
    shmem=$(stats_col "$nome" 11 mb)
    kernel=$(stats_col "$nome" 12 mb)
    slab=$(stats_col "$nome" 13 mb)
    nd=$(stats_nao_descartavel "$nome")
    oom=$(oom_total "$nome")
    restarts=$(restart_total "$nome")
    oomkilled=$(oom_killed_flag "$nome")
    pids=$(stats_col "$nome" 19 int)
    net_last=$(last_nonempty "$nome" 20)
    block_last=$(last_nonempty "$nome" 21)
    [ -n "$net_last" ] || net_last="n/d"
    [ -n "$block_last" ] || block_last="n/d"

    local cpu_tmp cpu_p50 cpu_p95 cpu_mx sums sum_per sum_nthr sum_thru razao thr_ms
    cpu_tmp="$TMP_DIR/cpu_${nome//[^A-Za-z0-9]/_}.txt"
    sums=$(cpu_and_throttle "$nome" "$cpu_tmp")
    read -r sum_per sum_nthr sum_thru <<< "$sums"
    local line
    line=$(percentiles_from_file "$cpu_tmp")
    IFS=$'\t' read -r cpu_p50 cpu_p95 cpu_mx <<< "$line"
    cpu_p50=$(round1 "$cpu_p50"); cpu_p95=$(round1 "$cpu_p95"); cpu_mx=$(round1 "$cpu_mx")
    rm -f "$cpu_tmp"

    if [ "$sum_per" -gt 0 ] 2>/dev/null; then
      razao=$(awk -v n="$sum_nthr" -v d="$sum_per" 'BEGIN{printf "%.1f%%", (n/d)*100}')
    else
      razao="n/d (sem período de CPU registrado — sem limite de cpus hoje)"
    fi
    thr_ms=$(awk -v u="$sum_thru" 'BEGIN{printf "%.0f", u/1000}')

    echo "| ${nome} | ${recriado} | ${mem_cur} | ${mem_peak} | ${anon} | ${file_} | ${shmem} | ${kernel} | ${slab} | ${nd} | ${oom} | ${restarts} | ${oomkilled} | ${cpu_p50}/${cpu_p95}/${cpu_mx} | ${razao} | ${thr_ms} | ${pids} | ${net_last} | ${block_last} |"
  done

  echo ""
  echo "### Host"
  echo ""
  echo "| Métrica | p50 | p95 | máximo |"
  echo "|---|---|---|---|"
  local col label col_label
  for col_label in "23:host_mem_used_mb (MB)" "24:host_mem_avail_mb (MB)" "25:host_swap_used_mb (MB)" "26:load1" "27:load5" "28:load15" "29:disk_used_% (/)"; do
    col="${col_label%%:*}"
    label="${col_label#*:}"
    local tmp line p50 p95 mx
    tmp="$TMP_DIR/host_${col}.txt"
    awk -F',' -v c="$col" '$4=="host" && $c!="" {print $c}' "$csv" > "$tmp"
    line=$(percentiles_from_file "$tmp")
    IFS=$'\t' read -r p50 p95 mx <<< "$line"
    rm -f "$tmp"
    echo "| ${label} | $(round1 "$p50") | $(round1 "$p95") | $(round1 "$mx") |"
  done

  echo ""
  echo "### Pico de build (dotnet/MSBuild/VBCSCompiler — sem limite de compose nenhum)"
  local build_tmp build_line build_p50 build_p95 build_mx
  build_tmp="$TMP_DIR/build.txt"
  awk -F',' '$4=="host" && $30!="" {print $30}' "$csv" > "$build_tmp"
  build_line=$(percentiles_from_file "$build_tmp")
  IFS=$'\t' read -r build_p50 build_p95 build_mx <<< "$build_line"
  rm -f "$build_tmp"
  if [ "$build_mx" = "n/d" ]; then
    echo "Nenhum processo de build detectado durante a janela: 0 MB."
  else
    echo "RSS somado (p50/p95/máx): $(awk -v v="$build_p50" 'BEGIN{printf "%.0f", v/1024}') / $(awk -v v="$build_p95" 'BEGIN{printf "%.0f", v/1024}') / $(awk -v v="$build_mx" 'BEGIN{printf "%.0f", v/1024}') MB."
  fi

  echo ""
  echo "### SonarQube"
  if echo "$nomes" | grep -qx "${SONAR_CONTAINER:-tesouro-direto-sonar}"; then
    echo "\`${SONAR_CONTAINER:-tesouro-direto-sonar}\` estava de pé durante a janela — ver linha dele na tabela de containers acima."
  else
    local sonar_last
    sonar_last=$(awk -F',' '$4=="host" && $31!="" {v=$31} END{print v}' "$csv")
    echo "\`${SONAR_CONTAINER:-tesouro-direto-sonar}\`: parado, 0 MB (status \`docker ps -a\`: ${sonar_last:-ausente})."
  fi
}

# ---------------------------------------------------------------------------
# Parsing de argumentos
# ---------------------------------------------------------------------------
WINDOW="$1"

SONAR_CONTAINER="${FOOTPRINT_SONAR_CONTAINER:-tesouro-direto-sonar}"
APP_CONTAINER="${FOOTPRINT_APP_CONTAINER:-tesouro-direto-app}"
STABLE_SAMPLES="${FOOTPRINT_DEPLOY_STABLE_SAMPLES:-6}"
DOCKER_TIMEOUT_S="${FOOTPRINT_DOCKER_TIMEOUT_S:-20}"
STATS_TIMEOUT_S="${FOOTPRINT_STATS_TIMEOUT_S:-180}"

# TMP_DIR único para todo o script (modo `summarize` isolado ou coleta ao
# vivo, que chama cmd_summarize no final) — um só lugar cria e um só trap limpa.
TMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/footprint.XXXXXX")
trap 'rm -rf "$TMP_DIR"' EXIT

if [ "$WINDOW" = "summarize" ]; then
  : "${2:?informe o arquivo.csv: run-footprint.sh summarize <arquivo.csv>}"
  cmd_summarize "$2"
  exit 0
fi

case "$WINDOW" in
  cold) DEFAULT_DURATION=300; DEFAULT_INTERVAL=5 ;;
  idle) DEFAULT_DURATION=3600; DEFAULT_INTERVAL=60 ;;
  load) DEFAULT_DURATION=900; DEFAULT_INTERVAL=5 ;;
  deploy) DEFAULT_DURATION=1800; DEFAULT_INTERVAL=5 ;;
  *)
    echo "ERRO: janela desconhecida '${WINDOW}'. Use cold|idle|load|deploy|summarize." >&2
    usage
    exit 1
    ;;
esac

DURATION_S="${2:-${FOOTPRINT_DURATION_S:-$DEFAULT_DURATION}}"
INTERVAL_S="${3:-${FOOTPRINT_INTERVAL_S:-$DEFAULT_INTERVAL}}"
LOAD_LABEL="${4:-${FOOTPRINT_LOAD_LABEL:-n/d}}"
LOAD_LABEL=$(printf '%s' "$LOAD_LABEL" | tr ',' ';')

if ! [[ "$DURATION_S" =~ ^[0-9]+$ ]] || [ "$DURATION_S" -le 0 ]; then
  echo "ERRO: duracao_s deve ser um inteiro positivo (recebido: '${DURATION_S}')." >&2
  exit 1
fi
if ! [[ "$INTERVAL_S" =~ ^[0-9]+$ ]] || [ "$INTERVAL_S" -le 0 ]; then
  echo "ERRO: intervalo_s deve ser um inteiro positivo (recebido: '${INTERVAL_S}')." >&2
  exit 1
fi

NETIO_INTERVAL_S="${FOOTPRINT_NETIO_INTERVAL_S:-30}"
NETIO_EVERY=$(( NETIO_INTERVAL_S / INTERVAL_S ))
[ "$NETIO_EVERY" -lt 1 ] && NETIO_EVERY=1

OUT_DIR="${FOOTPRINT_OUT_DIR:-/var/tmp/footprint}"
mkdir -p "$OUT_DIR"
CSV_FILE="$OUT_DIR/${WINDOW}-$(date +%Y%m%d-%H%M%S).csv"
csv_header > "$CSV_FILE"

echo "=== run-footprint.sh — janela '${WINDOW}' ===" >&2
echo "Duração alvo: ${DURATION_S}s | Intervalo: ${INTERVAL_S}s | Cadência NetIO/BlockIO: ~${NETIO_INTERVAL_S}s" >&2
[ "$WINDOW" = "load" ] && echo "Rótulo de carga: ${LOAD_LABEL}" >&2
echo "CSV bruto: ${CSV_FILE} (fica só na VPS, não sobe pelo stdout)" >&2
echo "" >&2

run_window

echo "" >&2
echo "Amostragem concluída. Motivo de parada: ${STOP_REASON}" >&2
echo "" >&2

cmd_summarize "$CSV_FILE"
