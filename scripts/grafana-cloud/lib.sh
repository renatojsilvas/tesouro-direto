#!/usr/bin/env bash
# Helpers para falar com a API do Grafana Cloud. Exige GC_GRAFANA_URL e GC_GRAFANA_TOKEN
# (service account token com role Admin na stack).
#
# Feito para `source`: nao chama exit no nivel do arquivo (so dentro de funcao, via
# return, para o chamador decidir o que fazer sob o `set -e` dele).
set -euo pipefail

gc_curl() {
  local method="$1" path="$2"; shift 2
  curl -sf -X "$method" \
    -H "Authorization: Bearer ${GC_GRAFANA_TOKEN:?defina GC_GRAFANA_TOKEN}" \
    -H 'Content-Type: application/json' \
    -H 'X-Disable-Provenance: true' \
    "${GC_GRAFANA_URL:?defina GC_GRAFANA_URL}${path}" "$@"
}

# Descobre o uid do datasource pelo TIPO — na nuvem os uids sao gerados
# (grafanacloud-<org>-prom), nunca os literais 'prometheus'/'loki' de casa.
#
# NUNCA escolher por posicao (`.[0]`). Confirmado ao vivo contra a stack
# zealouspike2240: ela tem MULTIPLOS datasources por tipo, e os primeiros da lista sao
# datasources META do Grafana Cloud, nao os nossos:
#   - tipo prometheus: 'grafanacloud-usage' (billing/uso) vem antes de
#     'grafanacloud-prom' (o nosso).
#   - tipo loki: 'grafanacloud-alert-state-history' vem antes de 'grafanacloud-logs'
#     (o nosso) e de 'grafanacloud-usage-insights'.
# Prova por query: `count by (job) (up)` em grafanacloud-prom devolve node e
# tesouro-direto-api; em grafanacloud-usage devolve VAZIO. Os labels de job em
# grafanacloud-logs sao kernel/nginx/tesouro-direto-api/tesouro-direto-web; em
# grafanacloud-alert-state-history, null. Ou seja: `.[0].uid` ligaria as 18 regras e
# as 59 referencias dos dashboards a datasources vazios — tudo muda/"No data" com
# HTTP 200 em cada etapa, o modo de falha silencioso que esta fase existe pra evitar.
#
# Estrategia: 1) resolver pelo uid CANONICO da stack, confirmando que ele existe na
# lista (nao por posicao); 2) se o canonico nao existir (outra stack, outro nome),
# cair para filtro por tipo excluindo os datasources meta conhecidos e abortar se
# sobrar mais de 1 candidato — ambiguidade nunca se resolve escolhendo o primeiro.
gc_datasource_uid() {
  local tipo="$1" canonico="" lista candidatos n achado

  case "$tipo" in
    prometheus) canonico="grafanacloud-prom" ;;
    loki)       canonico="grafanacloud-logs" ;;
  esac

  lista=$(gc_curl GET /api/datasources)

  if [ -n "$canonico" ]; then
    achado=$(echo "$lista" | jq -r --arg u "$canonico" 'map(select(.uid == $u)) | .[0].uid // empty')
    if [ -n "$achado" ]; then
      echo "$achado"
      return 0
    fi
  fi

  candidatos=$(echo "$lista" | jq -r --arg t "$tipo" \
    'map(select(.type == $t)
         | select(.uid | test("^grafanacloud-(usage|alert-state-history|usage-insights)$|-cardinality-management$") | not))
     | .[].uid')
  n=$(echo "$candidatos" | grep -c . || true)

  if [ "$n" -ne 1 ]; then
    echo "gc_datasource_uid: esperava exatamente 1 datasource do tipo '$tipo' apos excluir os meta da stack (uid canonico '$canonico' nao encontrado), achei $n" >&2
    return 1
  fi
  echo "$candidatos"
}

# Resolve o uid do datasource META de uso/billing — 'grafanacloud-usage' — que
# gc_datasource_uid EXCLUI deliberadamente (ver comentario acima: ele existe
# justamente para nao deixar esse datasource vazar para as regras que leem as
# metricas de negocio/infra da propria stack). As regras td-cloud-* precisam dele
# de proposito: e o UNICO lugar onde vivem active_series/overage/limits da conta.
#
# Mesma estrategia de resolucao por uid CANONICO das outras funcoes deste arquivo
# (nunca por posicao/`.[0]`): confirma que 'grafanacloud-usage' existe na lista de
# datasources antes de devolve-lo, em vez de assumir. Se o nome canonico mudar em
# outra stack, aborta em vez de adivinhar — o mesmo principio de
# gc_datasource_uid, so que para um uid especifico em vez de um tipo.
gc_datasource_uid_usage() {
  local canonico="grafanacloud-usage" lista achado

  lista=$(gc_curl GET /api/datasources)
  achado=$(echo "$lista" | jq -r --arg u "$canonico" 'map(select(.uid == $u)) | .[0].uid // empty')

  if [ -z "$achado" ]; then
    echo "gc_datasource_uid_usage: datasource '$canonico' nao encontrado na stack" >&2
    return 1
  fi
  echo "$achado"
}

# Cria a pasta se nao existir; devolve o uid nos dois casos (idempotente).
gc_folder_uid() {
  local titulo="$1" existente
  existente=$(gc_curl GET /api/folders | jq -r --arg t "$titulo" \
    'map(select(.title == $t)) | .[0].uid // empty')
  if [ -n "$existente" ]; then echo "$existente"; return; fi
  gc_curl POST /api/folders -d "$(jq -nc --arg t "$titulo" '{title: $t}')" | jq -r '.uid'
}
