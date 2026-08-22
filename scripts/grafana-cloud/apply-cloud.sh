#!/usr/bin/env bash
# Recria na nuvem o alerting versionado em infra/grafana/cloud/.
#
# A API de provisioning do Grafana Cloud NAO aceita o formato de arquivo do
# /export (confirmado ao vivo: POST /api/v1/provisioning/contact-points com o YAML
# 'apiVersion: 1 / contactPoints: [...]' devolve HTTP 400 "bad request data"). Esse
# formato so e consumivel pelo provisioning POR ARQUIVO — que e justamente o recurso
# que a nuvem nao tem. Por isso este script nao faz POST/PUT do YAML inteiro: ele
# decompoe cada arquivo na forma que a API de fato aceita (um EmbeddedContactPoint
# por receiver, uma arvore de rota sem envelope para policies, um grupo de regras
# por PUT). As formas abaixo foram confirmadas por sondagem HTTP real contra a
# stack, nao inferidas da documentacao.
#
# ORCAMENTO DE VERSAO: o free tier do Grafana Cloud guarda no maximo 10 VERSOES POR
# REGRA. Cada PUT de rule-group deste script gera uma versao nova para as regras
# daquele grupo — reaplicar o script em loop (ex.: um cron ou um retry automatico)
# consome esse orcamento e eventualmente perde o historico mais antigo. Rodar sob
# demanda, nao em loop.
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/grafana-cloud/lib.sh

DS_PROM=$(gc_datasource_uid prometheus)
DS_LOKI=$(gc_datasource_uid loki)
DS_USAGE=$(gc_datasource_uid_usage)
[ -n "$DS_PROM" ] || { echo "datasource prometheus nao encontrado na nuvem" >&2; exit 1; }
[ -n "$DS_LOKI" ] || { echo "datasource loki nao encontrado na nuvem" >&2; exit 1; }
[ -n "$DS_USAGE" ] || { echo "datasource grafanacloud-usage nao encontrado na nuvem" >&2; exit 1; }
echo "datasources: prom=$DS_PROM loki=$DS_LOKI usage=$DS_USAGE"

FOLDER_UID=$(gc_folder_uid TesouroDireto)
echo "folder TesouroDireto: $FOLDER_UID"

# Pasta separada para o Hub de Precos: o dashboard dele descreve OUTRO servico,
# versionado em outro repo (hub-precos). `gc_folder_uid` cria a pasta se ainda nao
# existir, entao nada precisa ser criado a mao no Grafana.
FOLDER_UID_HUB=$(gc_folder_uid HubPrecos)
echo "folder HubPrecos: $FOLDER_UID_HUB"

TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

sed -e "s/__DS_PROM__/${DS_PROM}/g" \
    -e "s/__DS_LOKI__/${DS_LOKI}/g" \
    -e "s/__DS_USAGE__/${DS_USAGE}/g" \
    infra/grafana/cloud/rules.yaml > "$TMP/rules.yaml"

# Rede de seguranca: o export-local.sh normaliza o bottoken (real ou '[REDACTED]',
# ver comentario la) para este placeholder. Se ele nao estiver aqui, o sed abaixo e
# um NO-OP silencioso — o contact point sobe na nuvem com o valor literal que estava
# no arquivo (as vezes '[REDACTED]') em vez do token real, e o Telegram nunca
# entrega, sem erro nenhum na criacao do contact point.
grep -q '\${TELEGRAM_BOT_TOKEN}' infra/grafana/cloud/contactpoints.yaml || {
  echo "ABORTADO: infra/grafana/cloud/contactpoints.yaml nao contem o placeholder \${TELEGRAM_BOT_TOKEN} — o sed seria um no-op e o Telegram ficaria mudo em silencio" >&2
  exit 1
}

# O contact point traz o token por env, igual ao provisioning local.
sed -e "s|\${TELEGRAM_BOT_TOKEN}|${TELEGRAM_BOT_TOKEN:?defina TELEGRAM_BOT_TOKEN}|g" \
    infra/grafana/cloud/contactpoints.yaml > "$TMP/contactpoints.yaml"

# Helper generico para POST/PUT de corpo JSON na API de provisioning (diferente do
# YAML de arquivo: este e o formato que a API realmente aceita).
provisioning_call() {
  local method="$1" path="$2" body="$3"
  curl -sf -X "$method" \
    -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
    -H 'Content-Type: application/json' \
    -H 'X-Disable-Provenance: true' \
    -d "$body" \
    "${GC_GRAFANA_URL}${path}"
}

# Converte duracao no estilo Go ("1m", "30s", "2h") — formato do campo `interval` no
# arquivo — para segundos inteiros, que e o que a API de rule-groups exige. ABORTA em
# vez de cair num default silencioso: um intervalo errado muda QUANDO a regra avalia
# (ex.: o `for: 1m` de td-db-readiness-down pressupoe scrape de 30s — um interval
# errado por default quebraria essa premissa sem avisar ninguem).
converter_intervalo_para_segundos() {
  local intervalo="$1"
  if [[ "$intervalo" =~ ^([0-9]+)h$ ]]; then
    echo $(( ${BASH_REMATCH[1]} * 3600 ))
  elif [[ "$intervalo" =~ ^([0-9]+)m$ ]]; then
    echo $(( ${BASH_REMATCH[1]} * 60 ))
  elif [[ "$intervalo" =~ ^([0-9]+)s$ ]]; then
    echo "${BASH_REMATCH[1]}"
  else
    echo "ABORTADO: intervalo de grupo '${intervalo}' em formato desconhecido (esperado Ns/Nm/Nh)" >&2
    return 1
  fi
}

# --- Contact points -----------------------------------------------------------------
#
# A API espera um EmbeddedContactPoint por receiver (uid/name/type/settings/
# disableResolveMessage), NAO a arvore {contactPoints:[{name, receivers:[...]}]} do
# arquivo — o `name` do contact point pai (ex.: "telegram-tesouro") tem de ser
# copiado para dentro de cada receiver. Iteramos TODOS os contactPoints x receivers,
# sem hardcode de indice — hoje e 1x1, mas o formato do arquivo permite mais.
#
# Idempotencia: um segundo POST com o mesmo uid provavelmente conflita (a API nao
# documenta upsert por POST). Consultamos os uids ja existentes uma vez e decidimos
# POST (criar) ou PUT .../contact-points/<uid> (atualizar) por receiver — rodar o
# script duas vezes seguidas tem de terminar 0 nas duas.
uids_cp_existentes=$(gc_curl GET /api/v1/provisioning/contact-points | jq -r '.[].uid')

n_cp=$(yq '.contactPoints | length' "$TMP/contactpoints.yaml")
for ((i = 0; i < n_cp; i++)); do
  n_recv=$(yq ".contactPoints[$i].receivers | length" "$TMP/contactpoints.yaml")
  for ((j = 0; j < n_recv; j++)); do
    recv_uid=$(yq -r ".contactPoints[$i].receivers[$j].uid" "$TMP/contactpoints.yaml")
    corpo=$(yq -o=json ".contactPoints[$i] as \$cp | .contactPoints[$i].receivers[$j] |
        {\"uid\": .uid, \"name\": \$cp.name, \"type\": .type, \"settings\": .settings, \"disableResolveMessage\": .disableResolveMessage}" \
      "$TMP/contactpoints.yaml")

    if echo "$uids_cp_existentes" | grep -qx "$recv_uid"; then
      provisioning_call PUT "/api/v1/provisioning/contact-points/${recv_uid}" "$corpo" >/dev/null
      echo "contact point ${recv_uid}: atualizado"
    else
      provisioning_call POST "/api/v1/provisioning/contact-points" "$corpo" >/dev/null
      echo "contact point ${recv_uid}: criado"
    fi
  done
done

# --- Notification policy -------------------------------------------------------------
#
# PUT recebe a arvore de rotas PURA (o objeto de dentro de `.policies[0]`), sem o
# envelope {apiVersion, policies:[...]} do arquivo e sem `orgId` (a stack da nuvem e
# de org unica; mandar orgId de outra org quebra o PUT).
politica_corpo=$(yq -o=json '.policies[0] | del(.orgId)' infra/grafana/cloud/policies.yaml)
provisioning_call PUT /api/v1/provisioning/policies "$politica_corpo" >/dev/null
echo "notification policy aplicada"

# --- Regras ---------------------------------------------------------------------------
#
# Nao existe POST em lote do arquivo inteiro. O que a API aceita e um PUT POR GRUPO:
# /api/v1/provisioning/folder/<FOLDER_UID>/rule-groups/<nome-do-grupo>, corpo
# {title, interval (segundos, int), rules: [...]} — cada regra precisa ganhar
# folderUID e ruleGroup, que nao existem no arquivo (o arquivo so faz sentido dentro
# de um group: no provisioning por arquivo; a API por grupo precisa dessa info
# explicita em cada regra). Iteramos TODOS os .groups[] do arquivo, sem hardcode do
# nome nem da contagem — hoje e so "tesouro-alertas".
#
# Este PUT e naturalmente idempotente (reaplica o grupo inteiro por cima).
n_grupos=$(yq '.groups | length' "$TMP/rules.yaml")
for ((gi = 0; gi < n_grupos; gi++)); do
  nome_grupo=$(yq -r ".groups[$gi].name" "$TMP/rules.yaml")
  intervalo_raw=$(yq -r ".groups[$gi].interval" "$TMP/rules.yaml")
  intervalo_seg=$(converter_intervalo_para_segundos "$intervalo_raw") || exit 1

  regras_grupo=$(yq -o=json ".groups[$gi].rules" "$TMP/rules.yaml" \
    | jq --arg fu "$FOLDER_UID" --arg rg "$nome_grupo" 'map(. + {folderUID: $fu, ruleGroup: $rg})')

  corpo_grupo=$(jq -n --arg t "$nome_grupo" --argjson interval "$intervalo_seg" --argjson rules "$regras_grupo" \
    '{title: $t, interval: $interval, rules: $rules}')

  provisioning_call PUT "/api/v1/provisioning/folder/${FOLDER_UID}/rule-groups/${nome_grupo}" "$corpo_grupo" >/dev/null
  echo "grupo de regras aplicado: ${nome_grupo} ($(echo "$regras_grupo" | jq 'length') regras, interval=${intervalo_seg}s)"
done

# --- Dashboards -------------------------------------------------------------------
#
# Dashboards no MESMO processo: DS_PROM/DS_LOKI/FOLDER_UID sao variaveis locais deste
# script. Um bloco bash separado nao as enxerga e `jq --arg p ""` gravaria uid vazio em
# todos os paineis com HTTP 200 — dashboard salvo e quebrado, sem erro nenhum.
# (Este endpoint e o de dashboards, /api/dashboards/db — nao e a API de provisioning,
# entao o formato de arquivo do /export nao se aplica aqui; sempre funcionou.)
# load-test NAO entra nesta lista. `load-test.json` le do Prometheus EFEMERO local
# (docker-compose.load.yml, sobe so sob `--profile load` durante o teste de carga,
# 127.0.0.1:9090). O backend SaaS do Grafana Cloud nao tem rota nenhuma para esse
# Prometheus e nunca tera — nao e um problema de rede resolvivel por tunel SSH (um
# tunel so da acesso a partir da MAQUINA de quem o abriu, nao a partir do backend da
# nuvem). Subir esse dashboard aqui produz um painel PERMANENTEMENTE vazio, calado, com
# HTTP 200 em cada etapa — exatamente o modo de falha que a tarefa 77 existe para
# eliminar. Mandar as series `k6_*` para a nuvem para contornar isso esta PROIBIDO por
# decisao registrada (cardinalidade alta e transiente; estouraria o teto de series do
# free tier justamente durante o teste de carga, perdendo as proprias metricas do
# teste — ver infra/alloy/README.md). NAO re-adicionar "load-test" a esta lista por
# simetria com os outros dois: o dado dele nunca vai existir aqui.
for d in tesouro-direto host; do
  jq --arg p "$DS_PROM" --arg l "$DS_LOKI" \
     'walk(if type=="object" and .uid=="prometheus" then .uid=$p
           elif type=="object" and .uid=="loki" then .uid=$l else . end)' \
     "infra/grafana/dashboards/$d.json" > "$TMP/$d.json"

  jq -nc --slurpfile db "$TMP/$d.json" --arg f "$FOLDER_UID" \
     '{dashboard: $db[0], folderUid: $f, overwrite: true}' \
    | curl -sf -X POST -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
        -H 'Content-Type: application/json' --data-binary @- \
        "${GC_GRAFANA_URL}/api/dashboards/db" | jq -r '.status + " " + .slug'
done

# --- Dashboard do Hub de Precos, em pasta propria ------------------------------------
#
# Fora do laco acima de proposito: mesmo tratamento de datasource, pasta DIFERENTE.
# O arquivo e versionado no repo hub-precos (infra/grafana/dashboards/hub-precos.json) e
# copiado para ca porque a publicacao mora neste repo -- as duas copias divergem em
# silencio se alguem editar so uma. Ver infra/grafana/README.md do hub-precos.
jq --arg p "$DS_PROM" --arg l "$DS_LOKI" \
   'walk(if type=="object" and .uid=="prometheus" then .uid=$p
         elif type=="object" and .uid=="loki" then .uid=$l else . end)' \
   infra/grafana/dashboards/hub-precos.json > "$TMP/hub-precos.json"

jq -nc --slurpfile db "$TMP/hub-precos.json" --arg f "$FOLDER_UID_HUB" \
   '{dashboard: $db[0], folderUid: $f, overwrite: true}' \
  | curl -sf -X POST -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
      -H 'Content-Type: application/json' --data-binary @- \
      "${GC_GRAFANA_URL}/api/dashboards/db" | jq -r '.status + " " + .slug'

# --- Convergencia: apaga da nuvem o load-test-k6 subido por engano na 77.3 -----------
#
# Antes desta fase o loop acima incluia "load-test" e ja rodou em producao (77.3) — o
# dashboard existe hoje na nuvem, permanentemente vazio (ver comentario acima). uid lido
# do proprio arquivo versionado (infra/grafana/dashboards/load-test.json, campo "uid" de
# topo), nunca chumbado aqui. DELETE por uid e idempotente do lado do RECURSO (o
# dashboard deixa de existir nas duas execucoes), mas nao do lado do HTTP: a 2a chamada
# volta 404. `curl -sf` trataria isso como falha (exit != 0) e abortaria o script inteiro
# sob `set -e` — por isso aqui capturamos so o codigo HTTP e tratamos 200 (apagou agora)
# e 404 (ja nao existia) como os DOIS resultados de sucesso; qualquer outro codigo e erro
# real.
uid_dashboard_k6=$(jq -r '.uid' infra/grafana/dashboards/load-test.json)
http_code_delete=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE \
  -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
  "${GC_GRAFANA_URL}/api/dashboards/uid/${uid_dashboard_k6}")
case "$http_code_delete" in
  200) echo "dashboard '${uid_dashboard_k6}': apagado da nuvem (era o load-test-k6 vazio herdado da 77.3)" ;;
  404) echo "dashboard '${uid_dashboard_k6}': ja nao existia na nuvem (convergido)" ;;
  *)
    echo "ABORTADO: DELETE do dashboard '${uid_dashboard_k6}' voltou HTTP ${http_code_delete} (esperado 200 ou 404)" >&2
    exit 1
    ;;
esac

# --- Verificacao final: regras --------------------------------------------------------
#
# HTTP 200/202 em cada PUT acima nao prova que as 21 regras existem nem que os
# datasources resolveram — confere de volta contra a API de leitura. (18 originais +
# 3 da tarefa 77.5: td-cloud-free-tier-series-proximo, td-cloud-overage,
# td-cloud-log-projecao-mensal, lendo o datasource 'grafanacloud-usage'.)
echo "conferindo regras aplicadas:"
regras_nuvem=$(gc_curl GET /api/v1/provisioning/alert-rules)
qtd_nuvem=$(echo "$regras_nuvem" | jq 'length')
echo "$qtd_nuvem"
if [ "$qtd_nuvem" -ne 21 ]; then
  echo "ABORTADO: a nuvem tem ${qtd_nuvem} regras de alerta, esperado 21" >&2
  exit 1
fi

# __expr__ e o pseudo-datasource do no de threshold (condition C de toda regra) —
# NAO e um datasource real e aparece em todas as 21 regras; nao e erro. So
# 'prometheus'/'loki' (uid de casa que nao existe na nuvem), uid vazio, e qualquer
# placeholder __DS_*__ remanescente (sed que falhou silenciosamente) sao erro.
# Medido ao vivo antes da tarefa 77.5: distribuicao era {__expr__: 18,
# grafanacloud-prom: 16, grafanacloud-logs: 2} no nivel data[], e
# {grafanacloud-logs: 2} no aninhado model.datasource.uid (so as 2 regras Loki tem
# essa posicao — mesma assimetria do export-local.sh). As 3 regras td-cloud-* somam
# __expr__: +3 e grafanacloud-usage: 3 (sem posicao aninhada — mesmo padrao das
# regras Prometheus existentes, que so tem model.datasource.uid nas regras Loki).
uids_ruins=$(echo "$regras_nuvem" | jq -r '
  [.[].data[] | (.datasourceUid, (.model.datasource.uid? // empty))]
  | map(select(. == "" or . == "prometheus" or . == "loki" or (test("^__DS_.*__$"))))
  | unique | .[]')
if [ -n "$uids_ruins" ]; then
  echo "ABORTADO: uid de datasource nao resolvido nas regras da nuvem: $(echo "$uids_ruins" | tr '\n' ' ')" >&2
  exit 1
fi
echo "distribuicao de datasourceUid na nuvem: $(echo "$regras_nuvem" | jq -c '[.[].data[].datasourceUid] | group_by(.) | map({(.[0] // "(vazio)"): length}) | add')"

# --- Verificacao final: dashboards -----------------------------------------------
#
# O modo de falha do bloco de dashboards acima e silencioso: o walk() so troca .uid
# quando o valor bate exatamente com "prometheus"/"loki"; se o schema do painel mudar
# (ex.: um datasource sem .uid, ou aninhado diferente) o walk simplesmente NAO acha
# nada para trocar, o POST volta 200 igual, e o dashboard fica com "No data" em
# producao sem nenhum sinal de erro no script. Por isso reconsultamos cada dashboard
# recem-criado pelo uid e conferimos que nenhum datasource ficou vazio ou com o
# literal antigo. Uids lidos direto dos JSONs versionados (campo "uid" de topo em
# infra/grafana/dashboards/*.json) — nao adivinhados.
verificar_datasources_resolvidos() {
  local uid="$1" literais
  literais=$(gc_curl GET "/api/dashboards/uid/${uid}" \
    | jq -r '[.dashboard | .. | objects | select(has("uid") and has("type")) | .uid] | unique | .[]')

  # Lista vazia NAO e "nada para reclamar" — e o proprio modo de falha que esta
  # verificacao existe para pegar (resposta com schema inesperado, walk() que nao
  # achou nenhum objeto de datasource). Uma verificacao que passa em silencio quando
  # nao acha nada e uma verificacao que nunca falha.
  if [ -z "$literais" ]; then
    echo "ABORTADO: dashboard '${uid}' nao trouxe nenhum objeto com uid/type de datasource — resposta inesperada, o mesmo modo de falha silenciosa que esta verificacao existe para pegar" >&2
    return 1
  fi

  while IFS= read -r u; do
    case "$u" in
      ""|prometheus|loki)
        echo "ABORTADO: dashboard '${uid}' voltou com datasource nao resolvido (uid='${u}') — POST deu 200 mas o dashboard esta quebrado" >&2
        return 1
        ;;
    esac
  done <<< "$literais"
}

# load-test-k6 fica de fora desta lista: nao sobe mais para a nuvem (e foi apagado de
# la, ver bloco de convergencia acima) — consulta-lo aqui devolveria 404 e abortaria
# o script sob `set -e`.
for uid in tesouro-direto-api host-node-exporter; do
  verificar_datasources_resolvidos "$uid"
done
echo "dashboards conferidos: datasources resolvidos em tesouro-direto-api, host-node-exporter"
