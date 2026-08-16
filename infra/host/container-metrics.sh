#!/bin/bash
set -euo pipefail

# container-metrics.sh — textfile collector para o node-exporter, tarefa 74.6.
#
# cAdvisor foi DESCARTADO (docs/PLANO.md, 74.6): custa 60-120 MB de RSS
# contra ~42 MB de orçamento no bloco INFRA. Este script lê cgroup v2 direto
# (mesma fonte que tests/load/profiling/run-footprint.sh já usa e valida) e
# escreve um `.prom` que o node-exporter serve via `--collector.textfile`.
# Roda a cada 30s por systemd timer (td-container-metrics.timer), casando
# com o `scrape_interval` do Prometheus (prometheus.yml:10).
#
# Cobre TODOS os containers em execução no host, não só os do compose deste
# projeto — o container-isca do gate de deploy não tem nome do projeto e
# ainda assim precisa aparecer.
#
# Memória: emite DUAS métricas de memória "quente", com papéis diferentes.
# `td_container_memory_working_set_bytes` (memory.current - inactive_file)
# é diagnóstico — comparável ao número do cAdvisor, útil em painel — mas
# inclui active_file (page cache RECLAMÁVEL, o kernel joga fora sob pressão
# sem matar ninguém). `td_container_memory_unreclaimable_bytes`
# (anon + shmem + (kernel - slab_reclaimable), memory.stat) é quem REGE os
# alertas de memória: os tetos da 74.3 foram dimensionados a partir do
# não-descartável, não do working_set — medido na VPS, alertar sobre
# working_set acusaria risco de OOM no node-exporter/promtail por causa de
# cache, não de pressão real. `kernel` inclui `slab`, que por sua vez inclui
# `slab_reclaimable` (dentry/inode cache) — memória que o kernel encolhe sob
# pressão ANTES de cogitar OOM, a mesma natureza do `active_file` acima
# (correção 80.2: contar slab_reclaimable do lado não-descartável inflava o
# número que rege os alertas de 85%/95% e que dimensionou os tetos da 74.3).

CGROUP_ROOT="${CGROUP_ROOT:-/sys/fs/cgroup}"
TEXTFILE_DIR="${TEXTFILE_DIR:-/var/lib/node_exporter/textfile}"
DOCKER_TIMEOUT_S="${DOCKER_TIMEOUT_S:-10}"
OUT_FILE="$TEXTFILE_DIR/container_resources.prom"

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

cgroup_path_for() {
  local full_id="$1"
  local p="$CGROUP_ROOT/system.slice/docker-${full_id}.scope"
  if [ -d "$p" ]; then
    printf '%s\n' "$p"
    return 0
  fi
  local found
  found=$(find "$CGROUP_ROOT" -maxdepth 6 -type d -name "docker-${full_id}.scope" 2>/dev/null | awk 'NR==1{print; exit}')
  if [ -n "$found" ]; then
    printf '%s\n' "$found"
    return 0
  fi
  return 1
}

read_kv() {
  # imprime o valor de "chave valor" num arquivo tipo memory.stat/cpu.stat/
  # memory.events e retorna 0; retorna 1 (SEM imprimir nada) se a chave não
  # existir ou o arquivo não for legível — ausência de dado não é zero (ver
  # regra dura da 74.6: 0 viraria divisão por zero na regra de alerta de
  # memory.max/working_set).
  local file="$1" key="$2"
  [ -r "$file" ] || return 1
  awk -v k="$key" '$1==k{print $2; f=1; exit} END{if(!f) exit 1}' "$file"
}

esc_label() {
  local s="$1"
  s="${s//\\/\\\\}"
  s="${s//\"/\\\"}"
  printf '%s' "$s"
}

if ! command -v docker >/dev/null 2>&1; then
  echo "ERRO: docker não encontrado no PATH — arquivo anterior preservado." >&2
  exit 1
fi

PS_OUT=""
if ! PS_OUT=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker ps --no-trunc --format '{{.ID}}\t{{.Names}}'); then
  echo "ERRO: 'docker ps' falhou ou expirou — arquivo anterior preservado." >&2
  exit 1
fi

CUR_IDS=()
CUR_NAMES=()
while IFS=$'\t' read -r id name; do
  [ -n "$id" ] || continue
  CUR_IDS+=("$id")
  CUR_NAMES+=("$name")
done <<< "$PS_OUT"

declare -A RESTART_MAP=()
if [ "${#CUR_IDS[@]}" -gt 0 ]; then
  # docker ps já respondeu; se ESTE inspect falhar não abortamos o script —
  # é falha parcial (só reinícios ficam sem dado), não "docker não
  # respondeu". Um único `docker inspect` com todos os IDs de uma vez.
  INSPECT_OUT=""
  if INSPECT_OUT=$(run_with_timeout "$DOCKER_TIMEOUT_S" docker inspect --format '{{.Id}}|{{.RestartCount}}' "${CUR_IDS[@]}" 2>/dev/null); then
    while IFS='|' read -r id rc; do
      [ -n "$id" ] || continue
      RESTART_MAP["$id"]="$rc"
    done <<< "$INSPECT_OUT"
  else
    echo "AVISO: 'docker inspect' falhou — td_container_restarts_total omitido nesta amostra." >&2
  fi
fi

M_WORKING=()
M_UNRECLAIMABLE=()
M_LIMIT=()
M_PEAK=()
M_PERIODS=()
M_THROTTLED=()
M_OOM=()
M_RECLAIM_EVENTS=()
M_RESTARTS=()
M_UP=()

for i in "${!CUR_NAMES[@]}"; do
  name="${CUR_NAMES[$i]}"
  id="${CUR_IDS[$i]}"
  label=$(esc_label "$name")

  M_UP+=("td_container_up{container=\"${label}\"} 1")

  if rc="${RESTART_MAP[$id]:-}"; [ -n "$rc" ]; then
    M_RESTARTS+=("td_container_restarts_total{container=\"${label}\"} ${rc}")
  fi

  cpath=""
  if ! cpath=$(cgroup_path_for "$id"); then
    continue
  fi

  mem_current=""
  inactive_file=""
  if mem_current=$(cat "$cpath/memory.current" 2>/dev/null) \
     && inactive_file=$(read_kv "$cpath/memory.stat" inactive_file); then
    working_set=$(( mem_current - inactive_file ))
    M_WORKING+=("td_container_memory_working_set_bytes{container=\"${label}\"} ${working_set}")
  fi

  # não-descartável = anon + shmem + (kernel - slab_reclaimable), memory.stat.
  # É o que rege os alertas de memória (rules.yaml): working_set acima inclui
  # active_file (page cache RECLAMÁVEL sob pressão, o kernel joga fora sem
  # matar ninguém), e os tetos da 74.3 foram dimensionados a partir do que
  # NÃO pode ser descartado. Medido na VPS (74.6): node-exporter e promtail
  # ficavam em 78%/70% do teto por working_set contra 45%/42% real por
  # não-descartável — o alerta de 85% acusaria risco de OOM por causa de
  # cache, não de pressão real.
  #
  # Correção 80.2: `kernel` já inclui `slab_reclaimable` (dentry/inode
  # cache) — mesma natureza do `active_file` acima: o kernel o encolhe sob
  # pressão ANTES de cogitar OOM. Contá-lo do lado não-descartável inflava o
  # número que rege os alertas de 85%/95% e que dimensionou os tetos da
  # 74.3. Impacto medido na VPS em 2026-08-16: app 117,0→115,7 MB (-1,1%,
  # 46%→45% do teto), web 80,4→79,9 MB (-0,6%, 50%→50%), db 43,2→42,8 MB
  # (-0,9%, 34%→33%), alloy 67,8→66,1 MB (-2,4%, 35%→34%) — nenhum container
  # muda de faixa de alerta, todos os deltas são para baixo e abaixo de
  # 2,5%. Isto NÃO é a causa do vazamento de ~0,4 MB/h do Alloy em regime —
  # a tarefa 80.2 segue aberta para isso.
  #
  # NÃO somar `slab` a este total: no cgroup v2 `slab` já está DENTRO de
  # `kernel` (verificado na tarefa 74.1, tests/load/profiling/run-footprint.sh,
  # função stats_nao_descartavel) — somar os dois conta slab em dobro. A
  # SUBTRAÇÃO de `slab_reclaimable` logo abaixo é uma operação diferente:
  # não soma nada de novo, só retira do `kernel` já contado a fatia que é
  # reclamável — o mesmo tipo de diferença que já separa `active_file` de
  # `inactive_file` no cálculo do working_set acima.
  #
  # Se anon, shmem ou kernel faltarem, omite a série inteira: ausência de
  # dado não é zero (mesma regra de read_kv acima). `slab_reclaimable` tem
  # tratamento diferente — ver comentário na leitura do campo, abaixo.
  mem_anon="" mem_shmem="" mem_kernel="" mem_slab_reclaimable=""
  if mem_anon=$(read_kv "$cpath/memory.stat" anon) \
     && mem_shmem=$(read_kv "$cpath/memory.stat" shmem) \
     && mem_kernel=$(read_kv "$cpath/memory.stat" kernel); then
    # `slab_reclaimable` pode faltar (cgroup v1, kernel antigo sem esse
    # campo em memory.stat). Ao contrário dos três campos acima, a ausência
    # AQUI não segue "ausência não é zero": omitir a série INTEIRA por causa
    # de um único campo secundário é PIOR do que cair para a fórmula antiga
    # — as 5 regras `td-container-*` e o dead-man
    # (`td-metricas-container-obsoletas`, `noDataState: Alerting`) ficariam
    # sem dado nenhum. Fallback para 0 equivale a "nada a descontar", ou
    # seja, exatamente `anon + shmem + kernel` — continua emitindo, nunca
    # omite por causa deste campo.
    mem_slab_reclaimable=$(read_kv "$cpath/memory.stat" slab_reclaimable) || mem_slab_reclaimable=0
    unreclaimable=$(( mem_anon + mem_shmem + (mem_kernel - mem_slab_reclaimable) ))
    M_UNRECLAIMABLE+=("td_container_memory_unreclaimable_bytes{container=\"${label}\"} ${unreclaimable}")
  fi

  mem_max=""
  if mem_max=$(cat "$cpath/memory.max" 2>/dev/null) && [ "$mem_max" != "max" ]; then
    M_LIMIT+=("td_container_memory_limit_bytes{container=\"${label}\"} ${mem_max}")
  fi

  mem_peak=""
  if mem_peak=$(cat "$cpath/memory.peak" 2>/dev/null); then
    M_PEAK+=("td_container_memory_peak_bytes{container=\"${label}\"} ${mem_peak}")
  fi

  nr_periods=""
  if nr_periods=$(read_kv "$cpath/cpu.stat" nr_periods); then
    M_PERIODS+=("td_container_cpu_cfs_periods_total{container=\"${label}\"} ${nr_periods}")
  fi

  nr_throttled=""
  if nr_throttled=$(read_kv "$cpath/cpu.stat" nr_throttled); then
    M_THROTTLED+=("td_container_cpu_cfs_throttled_periods_total{container=\"${label}\"} ${nr_throttled}")
  fi

  oom_kill=""
  if oom_kill=$(read_kv "$cpath/memory.events" oom_kill); then
    M_OOM+=("td_container_oom_kill_total{container=\"${label}\"} ${oom_kill}")
  fi

  # campo `max` do memory.events: quantas vezes uma alocação bateu no teto
  # (memory.max) e forçou o kernel a fazer reclaim ANTES de cogitar o OOM
  # killer. Medido na VPS (74.6): zero nos oito containers com os tetos já
  # corrigidos pela 74.3 — foi este mesmo contador que, antes da correção,
  # acusou os 9131 eventos do `grafana` mal dimensionado. Linha de base
  # zero é o que torna um limiar sustentado (regra de alerta) um sinal
  # honesto, em vez de um limiar chutado sobre uma razão.
  reclaim_events=""
  if reclaim_events=$(read_kv "$cpath/memory.events" max); then
    M_RECLAIM_EVENTS+=("td_container_memory_reclaim_events_total{container=\"${label}\"} ${reclaim_events}")
  fi
done

emit_block() {
  # SEMPRE retorna 0 — este é o último comando do grupo `{ ... } >
  # "$TMP_FILE"` em pelo menos uma chamada (td_container_up), e sob `set -e`
  # um grupo de comandos herda o status de saída do seu último comando: se
  # `emit_block` pudesse terminar com status 1 (ex.: nenhum container
  # presente, lista vazia), o `set -e` abortaria o script ANTES do `mv`,
  # sem erro legível e deixando o `.prom.$$` órfão. Por isso o `return 0`
  # explícito no fim, além do `return 0` do caminho "lista vazia" — nenhum
  # dos dois caminhos deste corpo deve terminar em status não-zero.
  local metric="$1" type="$2" help="$3"
  shift 3
  local -a lines=("$@")
  if [ "${#lines[@]}" -eq 0 ]; then
    return 0
  fi
  printf '# HELP %s %s\n' "$metric" "$help"
  printf '# TYPE %s %s\n' "$metric" "$type"
  local l
  for l in "${lines[@]}"; do
    printf '%s\n' "$l"
  done
  return 0
}

mkdir -p "$TEXTFILE_DIR"
TMP_FILE="$TEXTFILE_DIR/container_resources.prom.$$"
# Se o script for morto entre o `>` abaixo e o `mv` final (SIGTERM de
# TimeoutStartSec, reboot, journal cheio), o temporário fica órfão em
# $TEXTFILE_DIR — o node-exporter só lê `*.prom`, então isso cresceria em
# silêncio para sempre. `rm -f` é inofensivo depois de um `mv` bem-sucedido
# (o caminho já não existe mais nesse ponto).
trap 'rm -f "$TMP_FILE"' EXIT

# "${ARR[@]+"${ARR[@]}"}" em vez de "${ARR[@]}": em bash < 4.4 (ex.: 3.2, o
# `/bin/bash` padrão do macOS) expandir um array VAZIO com `set -u` é
# "unbound variable" e aborta o script — já reproduzido neste host. A forma
# `${ARR[@]+...}` só expande o `...` se `ARR[@]` estiver definido (mesmo
# vazio conta como definido para um array declarado), e produz zero
# argumentos quando o array está vazio, sem erro em nenhuma versão de bash.
{
  emit_block td_container_memory_working_set_bytes gauge \
    "memoria quente do container: memory.current - inactive_file (memory.stat), bytes. DIAGNOSTICO (comparavel ao numero do cAdvisor) — nao rege alerta, ver td_container_memory_unreclaimable_bytes." \
    "${M_WORKING[@]+"${M_WORKING[@]}"}"
  emit_block td_container_memory_unreclaimable_bytes gauge \
    "memoria nao-descartavel do container: anon + shmem + (kernel - slab_reclaimable) (memory.stat), bytes. Rege os alertas de memoria (rules.yaml) — os tetos da 74.3 foram dimensionados a partir deste numero, nao do working_set (que inclui page cache reclamavel). slab_reclaimable e dentry/inode cache que o kernel encolhe sob pressao antes de cogitar OOM; se ausente (cgroup v1/kernel antigo), o campo cai para 0 (formula anon+shmem+kernel) em vez de omitir a serie." \
    "${M_UNRECLAIMABLE[@]+"${M_UNRECLAIMABLE[@]}"}"
  emit_block td_container_memory_limit_bytes gauge \
    "teto de memoria do container (memory.max), bytes. Ausente quando o container nao tem limite (memory.max=max)." \
    "${M_LIMIT[@]+"${M_LIMIT[@]}"}"
  emit_block td_container_memory_peak_bytes gauge \
    "pico de memoria do container desde a criacao do cgroup (memory.peak), bytes." \
    "${M_PEAK[@]+"${M_PEAK[@]}"}"
  emit_block td_container_cpu_cfs_periods_total counter \
    "numero de periodos do CFS observados para o container (cpu.stat nr_periods)." \
    "${M_PERIODS[@]+"${M_PERIODS[@]}"}"
  emit_block td_container_cpu_cfs_throttled_periods_total counter \
    "numero de periodos do CFS em que o container foi estrangulado (cpu.stat nr_throttled)." \
    "${M_THROTTLED[@]+"${M_THROTTLED[@]}"}"
  emit_block td_container_oom_kill_total counter \
    "numero de OOM kills no cgroup do container (memory.events oom_kill)." \
    "${M_OOM[@]+"${M_OOM[@]}"}"
  emit_block td_container_memory_reclaim_events_total counter \
    "numero de vezes que uma alocacao bateu no teto de memoria e forcou reclaim (memory.events campo max). Linha de base medida e zero nos oito containers com os tetos da 74.3 corrigidos." \
    "${M_RECLAIM_EVENTS[@]+"${M_RECLAIM_EVENTS[@]}"}"
  emit_block td_container_restarts_total counter \
    "numero de reinicios do container, do docker inspect (.RestartCount)." \
    "${M_RESTARTS[@]+"${M_RESTARTS[@]}"}"
  emit_block td_container_up gauge \
    "1 para cada container em execucao visto nesta amostra." \
    "${M_UP[@]+"${M_UP[@]}"}"
} > "$TMP_FILE"

mv -f "$TMP_FILE" "$OUT_FILE"
