#!/bin/bash
set -euo pipefail

usage() {
  echo "Uso: $0 <script-k6> [--local]"
  echo "  <script-k6>   caminho do script k6 a executar, ex.: tests/load/api/titulos.js"
  echo "  --local       sobe o overlay docker-compose.load.yml (Prometheus remote-write receiver local)"
  echo ""
  echo "Env obrigatórias (sem default; nunca aponta para produção sem ALLOW_PROD=1):"
  echo "  API_URL   URL base da API — obrigatória para cenários em tests/load/api/*, a menos que WEB_URL esteja definida"
  echo "  WEB_URL   URL base do site Blazor — obrigatória para o cenário de site, a menos que API_URL esteja definida"
  echo "  API_KEY   chave de API usada nas requisições"
  echo ""
  echo "Env opcional:"
  echo "  CLIENT_API_KEY               chave de cliente, encaminhada ao script se definida (ex.: rate-limit/validate.js)"
  echo "  K6_PROMETHEUS_RW_SERVER_URL  destino do remote-write do k6; OBRIGATÓRIA quando --local não é usado (sem default)"
  echo "  ALLOW_PROD=1                 único jeito de permitir alvo em dadosdotesourodireto.com.br (não recomendado)"
}

if [ "${1:-}" = "" ] || [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  echo "ERRO: informe o script k6 a executar (alvo obrigatório) — nenhum default." >&2
  echo "" >&2
  usage
  exit 1
fi

SCRIPT_ARG="$1"
shift

LOCAL=0
for arg in "$@"; do
  if [ "$arg" = "--local" ]; then
    LOCAL=1
  fi
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_PATH="$SCRIPT_DIR/$SCRIPT_ARG"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"
LOAD_COMPOSE_FILE="$SCRIPT_DIR/docker-compose.load.yml"

if [ ! -f "$SCRIPT_PATH" ]; then
  echo "ERRO: script k6 não encontrado em $SCRIPT_PATH" >&2
  exit 1
fi

if [ -z "${API_URL:-}" ] && [ -z "${WEB_URL:-}" ]; then
  echo "ERRO: defina API_URL (cenários de API) ou WEB_URL (cenário de site) — sem default." >&2
  usage
  exit 1
fi

if [ -z "${API_KEY:-}" ]; then
  echo "ERRO: defina API_KEY — sem default." >&2
  usage
  exit 1
fi

TARGET_URL="${API_URL:-${WEB_URL:-}}"
if echo "$TARGET_URL" | grep -q "dadosdotesourodireto.com.br" && [ "${ALLOW_PROD:-0}" != "1" ]; then
  echo "ERRO: o alvo ($TARGET_URL) aponta para produção — bloqueado por padrão." >&2
  echo "Defina ALLOW_PROD=1 explicitamente se você tem CERTEZA que quer isso (não recomendado)." >&2
  exit 1
fi

export K6_PROMETHEUS_RW_TREND_STATS="p(95),p(99),avg,max,min"
echo "K6_PROMETHEUS_RW_TREND_STATS=$K6_PROMETHEUS_RW_TREND_STATS"

if [ "$LOCAL" = "1" ]; then
  if [ -z "${K6_PROMETHEUS_RW_SERVER_URL:-}" ]; then
    export K6_PROMETHEUS_RW_SERVER_URL="http://localhost:9090/prometheus/api/v1/write"
    echo "K6_PROMETHEUS_RW_SERVER_URL não definida — usando default local (--local): $K6_PROMETHEUS_RW_SERVER_URL"
  fi
  echo "Subindo overlay do Prometheus com remote-write receiver habilitado..."
  # AVISO OPERACIONAL (77.4): se este script rodar a partir de /opt/tesouro-direto na
  # propria VPS, `prometheus` e `k6` nascem sob o MESMO project name do compose de
  # producao, e um push em main no meio do teste MATA os dois — por caminhos
  # diferentes, o que importa saber ao depurar: o `k6` nem existe no compose que o
  # deploy usa (so no overlay `load`), entao cai no `--remove-orphans`; o `prometheus`
  # existe, so esta desabilitado por profile, entao NAO e orfao — quem o recolhe e o
  # `docker compose --profile load rm -sf prometheus` logo depois
  # (.github/workflows/deploy.yml). So o teste se perde (nada de producao), mas nao
  # vale descobrir isso no meio de uma medicao — nao rode `--local` na VPS dentro de
  # uma janela de deploy.
  docker compose -f "$COMPOSE_FILE" -f "$LOAD_COMPOSE_FILE" --profile load up -d prometheus
else
  if [ -z "${K6_PROMETHEUS_RW_SERVER_URL:-}" ]; then
    echo "ERRO: defina K6_PROMETHEUS_RW_SERVER_URL explicitamente (sem default) quando não usar --local." >&2
    exit 1
  fi
fi

if ! command -v k6 >/dev/null 2>&1; then
  echo "ERRO: k6 não está instalado nesta máquina." >&2
  echo "Instale com 'brew install k6' ou use o serviço k6 do overlay (mesma rede tesouro-net):" >&2
  echo "  docker compose -f docker-compose.yml -f docker-compose.load.yml run --rm k6 run -o experimental-prometheus-rw -e API_URL=... -e API_KEY=... /scripts/<caminho-em-tests-load>" >&2
  exit 1
fi

K6_ARGS=(-e "API_KEY=$API_KEY")
if [ -n "${API_URL:-}" ]; then
  K6_ARGS+=(-e "API_URL=$API_URL")
fi
if [ -n "${WEB_URL:-}" ]; then
  K6_ARGS+=(-e "WEB_URL=$WEB_URL")
fi
if [ -n "${CLIENT_API_KEY:-}" ]; then
  K6_ARGS+=(-e "CLIENT_API_KEY=$CLIENT_API_KEY")
fi
if [ -n "${ALLOW_PROD:-}" ]; then
  K6_ARGS+=(-e "ALLOW_PROD=$ALLOW_PROD")
fi

echo "Executando $SCRIPT_ARG contra $TARGET_URL ..."
k6 run -o experimental-prometheus-rw "${K6_ARGS[@]}" "$SCRIPT_PATH"

# Nao existe (e nao pode existir) dashboard do k6 no Grafana Cloud: as series k6_* sao
# proibidas na nuvem (cardinalidade alta e transiente estouraria o teto de series
# justamente durante o teste, perdendo as proprias metricas dele — ver infra/alloy/
# README.md) e o `load-test-k6` que chegou a subir la na 77.3 foi removido por
# convergencia (scripts/grafana-cloud/apply-cloud.sh). Os dados do k6 sempre vivem no
# destino do remote-write, nunca na nuvem — aponte para lá, nao para um painel que
# nunca tera dado.
if [ "$LOCAL" = "1" ]; then
  echo "Métricas do k6: Prometheus efêmero local em http://localhost:9090/prometheus/ (sobe só sob --profile load; sem Grafana neste modo — use a UI/API do próprio Prometheus)."
else
  echo "Métricas do k6: enviadas para $K6_PROMETHEUS_RW_SERVER_URL — consulte o Prometheus/Grafana desse destino diretamente; não existe dashboard do k6 no Grafana Cloud."
fi
