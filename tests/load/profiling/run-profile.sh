#!/bin/bash
set -euo pipefail

usage() {
  echo "Uso: $0 [FLOW] [VUS] [DURATION]"
  echo "  FLOW       titulos|historico|preco-atual|simulador (default: titulos)"
  echo "  VUS        número de VUs constantes (default: 24)"
  echo "  DURATION   duração da carga, formato k6 (default: 90s)"
  echo ""
  echo "Env obrigatórias (sem default; nunca aponta produção sem ALLOW_PROD=1):"
  echo "  API_URL   URL base da API (ex.: http://localhost:5000)"
  echo "  API_KEY   service key usada via X-Api-Key"
  echo ""
  echo "Pressupõe o stack de profiling já no ar. Ver docs/load/profiling.md."
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

: "${API_URL:?defina API_URL (ex. http://localhost:5000) — sem default; nunca aponta produção sem ALLOW_PROD=1}"
: "${API_KEY:?defina API_KEY (service key) — sem default}"

if echo "$API_URL" | grep -q "dadosdotesourodireto.com.br" && [ "${ALLOW_PROD:-0}" != "1" ]; then
  echo "ERRO: o alvo ($API_URL) aponta para produção — bloqueado por padrão." >&2
  echo "Defina ALLOW_PROD=1 explicitamente se você tem CERTEZA que quer isso (profiling em prod é invasivo)." >&2
  exit 1
fi

FLOW="${1:-titulos}"
VUS="${2:-24}"
DURATION="${3:-90s}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
STEADY_SCRIPT="$SCRIPT_DIR/steady.js"
OUT_DIR="$REPO_ROOT/scratchpad"

DB_CONTAINER="tesouro-direto-db"
APP_CONTAINER="tesouro-direto-app"
# 79-A.2: `CREATE EXTENSION pg_stat_statements` e `pg_stat_statements_reset()`
# exigem SUPERUSER -- `td_app` e NOSUPERUSER (infra/postgres/sql/td-app-role.sql)
# e nao pode rodar nenhum dos dois. O profiling passa a usar a role admin.
# `DB_ADMIN_USER` e sobrescritivel por env porque num cluster legado
# (pre-79-A.3, volume `pgdata` ja inicializado antes desta fase) o admin
# ainda se chama `app` -- ver achado 3 do PLANO.
DB_ADMIN_USER="${DB_ADMIN_USER:-postgres}"
DB_NAME="tesouro_direto"
# Senha da role admin (postgres/app, conforme DB_ADMIN_USER acima) -- nunca da
# aplicacao, que conecta como td_app.
DB_PASSWORD_VALUE="${DB_PASSWORD:-app123}"

SAMPLES_FILE="$OUT_DIR/profile-$FLOW-samples.txt"
SUMMARY_FILE="$OUT_DIR/profile-$FLOW.json"
LOG_FILE="$OUT_DIR/profile-$FLOW.log"
PGSTAT_FILE="$OUT_DIR/profile-$FLOW-pgstat.txt"

echo "=== Profiling sob carga: k6 + /metrics do app + pg_stat_statements ==="
echo "Alvo:      $API_URL"
echo "Fluxo:     $FLOW"
echo "VUs:       $VUS"
echo "Duração:   $DURATION"
echo "Artefatos: $OUT_DIR/profile-$FLOW.*"
echo ""

mkdir -p "$OUT_DIR"

echo "Checando API ($API_URL/health)..."
if ! curl -sf "$API_URL/health" > /dev/null 2>&1; then
  echo "ERRO: API não respondeu em $API_URL/health." >&2
  echo "Suba o stack de profiling antes de rodar este script:" >&2
  echo "  PROFILE_CPUS=1 docker compose -f docker-compose.yml -f docker-compose.profiling.yml up -d db app" >&2
  exit 1
fi
echo "API respondeu."

echo "Habilitando pg_stat_statements e zerando estatísticas..."
docker exec -e PGPASSWORD="$DB_PASSWORD_VALUE" "$DB_CONTAINER" \
  psql -U "$DB_ADMIN_USER" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;" > /dev/null 2>&1 || true
docker exec -e PGPASSWORD="$DB_PASSWORD_VALUE" "$DB_CONTAINER" \
  psql -U "$DB_ADMIN_USER" -d "$DB_NAME" -c "SELECT pg_stat_statements_reset();" > /dev/null

grab() {
  curl -s "$API_URL/metrics" | grep -E "system_runtime_threadpool_queue_length|system_runtime_threadpool_thread_count|process_cpu_seconds_total|system_runtime_total_pause_time_by_gc_total|system_runtime_gc_heap_size|dotnet_total_memory_bytes" | grep -vE "^#"
}

echo "T0 $(date +%s)" > "$SAMPLES_FILE"
grab >> "$SAMPLES_FILE"

echo "Iniciando amostragem de /metrics (a cada 4s) e docker stats..."
(
  for _ in $(seq 1 40); do
    echo "T=$(date +%s)"
    grab
    docker stats --no-stream --format '{{.CPUPerc}} {{.MemUsage}}' "$APP_CONTAINER" 2>/dev/null | sed 's/^/DSTAT /' || true
    sleep 4
  done >> "$SAMPLES_FILE"
) &
SAMPLER_PID=$!

echo "Rodando k6 (flow=$FLOW vus=$VUS duration=$DURATION)..."
VUS="$VUS" DURATION="$DURATION" FLOW="$FLOW" API_KEY="$API_KEY" k6 run \
  -e API_URL="$API_URL" -e API_KEY="$API_KEY" -e VUS="$VUS" -e DURATION="$DURATION" -e FLOW="$FLOW" \
  --summary-export="$SUMMARY_FILE" \
  "$STEADY_SCRIPT" > "$LOG_FILE" 2>&1 || true

kill "$SAMPLER_PID" 2>/dev/null || true
echo "T1 $(date +%s)" >> "$SAMPLES_FILE"
grab >> "$SAMPLES_FILE"

echo "Coletando top de queries do Postgres (pg_stat_statements)..."
docker exec -e PGPASSWORD="$DB_PASSWORD_VALUE" "$DB_CONTAINER" \
  psql -U "$DB_ADMIN_USER" -d "$DB_NAME" -A -F'|' -c "SELECT calls, round(total_exec_time::numeric,0) total_ms, round(mean_exec_time::numeric,2) mean_ms, round((100*total_exec_time/NULLIF(sum(total_exec_time) over (),0))::numeric,1) pct, left(regexp_replace(query,'\s+',' ','g'),90) q FROM pg_stat_statements WHERE query NOT LIKE '%pg_stat_statements%' ORDER BY total_exec_time DESC LIMIT 15;" \
  > "$PGSTAT_FILE" 2>&1

echo ""
echo "=== Artefatos gerados ==="
echo "Amostras /metrics + docker stats: $SAMPLES_FILE"
echo "Resumo k6:                        $SUMMARY_FILE"
echo "Log k6:                           $LOG_FILE"
echo "Top queries Postgres:             $PGSTAT_FILE"
echo ""
echo "Como ler cada artefato: docs/load/profiling.md (seção 'Como ler os resultados')."
