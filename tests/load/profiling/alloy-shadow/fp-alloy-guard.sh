#!/bin/bash
# Guarda de memoria da fase 77.0 (tesouro-direto-alloy).
#
# CORRECAO (revisao adversarial do portao 77.0, 2026-08-15): a formula da
# nao-descartavel usada por este script ERA anon+shmem+slab, copiada de
# /root/fp-gate76.sh. Essa formula esta ERRADA. Corrigida aqui para
# anon+shmem+kernel — e esse script tem um comentario explicito avisando que
# `slab` NAO deve ser somado, pois no cgroup v2 `slab` ja esta DENTRO de
# `kernel` (verificado na 74.1, tests/load/profiling/run-footprint.sh, funcao
# stats_nao_descartavel): somar os dois conta slab em dobro. NAO reverta isto
# para `slab` — a fonte de verdade e infra/host/container-metrics.sh, nao
# fp-gate76.sh (que carrega o mesmo defeito e NAO foi corrigido, porque a
# janela de 24h do gate da tarefa 76 ja tinha fechado quando o defeito foi
# achado).
#
# DIVERGENCIA PROPOSITAL (revisao adversarial da 80.2, 2026-08-16): a partir
# dessa fase, `infra/host/container-metrics.sh` passou a usar
# anon+shmem+(kernel-slab_reclaimable) — mais precisa, pois slab_reclaimable
# volta ao sistema sob pressao sem OOM. ESTE script (e run-footprint.sh)
# NAO acompanham essa mudanca de proposito: mudar a formula quebraria a
# comparabilidade com todas as medicoes historicas ja registradas no PLANO
# (74.x, 76, 77.x), feitas com anon+shmem+kernel. anon+shmem+kernel NAO e
# mais "a formula canonica do projeto" (isso mudou na 80.2) — e sim a formula
# historica deste andaime de medicao, mantida deliberadamente por esse
# motivo. Fonte de verdade para producao: infra/host/container-metrics.sh.
#
# Corte de seguranca em 500 MB, NAO o teto de decisao (350 MB) da 77.0:
# o teto de decisao fica na tabela de decisao do runbook da 77 e e' avaliado
# por quem le o CSV depois. Um corte automatico neste script em 350 MB
# enviesaria a propria medição que a fase 77.0 quer fazer (o container
# pararia antes de mostrar o pico real). 500 MB e' só uma rede de protecao
# contra vazamento descontrolado: qualquer valor que a acione ja cai, de
# sobra, na faixa "para" da tabela de decisao da 77.0 — ou seja, o corte
# não pode mudar a decisão, só evita OOM do host.
set -euo pipefail

CONTAINER="tesouro-direto-alloy-shadow"
CSV=/var/tmp/fp-alloy.csv
LIMITE_BYTES=$((500 * 1024 * 1024))

# Container ainda nao subiu (cenario esperado antes da entrega das
# credenciais): sai em silencio, sem sujar o CSV.
docker inspect "$CONTAINER" >/dev/null 2>&1 || exit 0

[ -f "$CSV" ] || echo "ts,container,nao_descartavel,mem_current,mem_max,oom_kill,restarts,motivo" > "$CSV"

TS=$(date -u +%s)
PID=$(docker inspect -f "{{.State.Pid}}" "$CONTAINER" 2>/dev/null) || exit 0
[ "$PID" = "0" ] && exit 0

REL=$(awk -F: '/^0::/{print $3}' /proc/$PID/cgroup 2>/dev/null)
CG="/sys/fs/cgroup$REL"
[ -d "$CG" ] || exit 0

CUR=$(cat "$CG/memory.current" 2>/dev/null || echo 0)
MAX=$(cat "$CG/memory.max" 2>/dev/null || echo 0)
# nao-descartavel = anon + shmem + kernel (memory.stat) — NAO somar `slab`
# (ja incluido em `kernel`). Formula historica deste andaime (ver nota de
# divergencia proposital no topo do arquivo); desde a 80.2,
# infra/host/container-metrics.sh usa anon+shmem+(kernel-slab_reclaimable).
AN=$(awk '/^anon /{print $2}' "$CG/memory.stat" 2>/dev/null || echo 0)
SH=$(awk '/^shmem /{print $2}' "$CG/memory.stat" 2>/dev/null || echo 0)
KE=$(awk '/^kernel /{print $2}' "$CG/memory.stat" 2>/dev/null || echo 0)
ND=$(( ${AN:-0} + ${SH:-0} + ${KE:-0} ))
OK=$(awk '/^oom_kill /{print $2}' "$CG/memory.events" 2>/dev/null || echo 0)
RS=$(docker inspect -f "{{.RestartCount}}" "$CONTAINER" 2>/dev/null || echo -1)

MOTIVO=""
if [ "$ND" -gt "$LIMITE_BYTES" ]; then
  MOTIVO="stop_nao_descartavel_acima_500mb"
  docker stop "$CONTAINER" >/dev/null 2>&1 || true
fi

echo "$TS,$CONTAINER,$ND,$CUR,$MAX,${OK:-0},$RS,$MOTIVO" >> "$CSV"
