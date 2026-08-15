#!/usr/bin/env bash
# Exporta alerting do Grafana LOCAL no formato de provisioning, para virar fonte da
# verdade em infra/grafana/cloud/. Rodar com um tunel SSH aberto para a VPS, usando a
# chave dedicada (nao a default do usuario):
#   ssh -N -i ~/.ssh/id_ed25519_vps -L 3000:127.0.0.1:3000 root@157.230.148.98
#
# GRAFANA_URL ja inclui o sub-path /grafana: o compose sobe com
# GF_SERVER_SERVE_FROM_SUB_PATH=true (docker-compose.yml), entao a API nao vive na raiz
# do host — bater em http://127.0.0.1:3000/api/... sem o /grafana da 404.
set -euo pipefail

# Ancora no root do repo, igual ao irmao apply-cloud.sh. Sem isto, OUT (relativo)
# resolve contra o CWD de quem chama o script, nao contra o repo — rodado de
# qualquer diretorio que nao seja a raiz, ele cria uma arvore infra/grafana/cloud/
# NO LUGAR ERRADO e escreve o export ali. Nenhum assert interno pega esse erro,
# porque eles conferem o arquivo que o proprio script acabou de escrever: tudo
# passa verde, o operador acha que exportou, e o repo continua com o rules.yaml
# velho. cd na raiz do repo fecha essa lacuna sem tirar a possibilidade de
# sobrescrever o destino via OUT= (que continua aceitando caminho absoluto).
cd "$(dirname "$0")/../.."

command -v yq >/dev/null 2>&1 || { echo "yq nao encontrado no PATH — obrigatorio (nao usamos sed para andar a arvore de UIDs)" >&2; exit 1; }

GRAFANA_URL="${GRAFANA_URL:-http://127.0.0.1:3000/grafana}"
GRAFANA_USER="${GRAFANA_USER:-admin}"
GRAFANA_PASSWORD="${GRAFANA_PASSWORD:?defina GRAFANA_PASSWORD}"
OUT="${OUT:-infra/grafana/cloud}"

mkdir -p "$OUT"

fetch() {
  curl -sfu "${GRAFANA_USER}:${GRAFANA_PASSWORD}" \
    -H 'Accept: application/yaml' \
    "${GRAFANA_URL}$1"
}

fetch /api/v1/provisioning/alert-rules/export   > "$OUT/rules.yaml"
fetch /api/v1/provisioning/contact-points/export > "$OUT/contactpoints.yaml"
fetch /api/v1/provisioning/policies/export       > "$OUT/policies.yaml"

# [[:space:]], nao \s: o grep do BSD (macOS sem ugrep/gnu-grep no PATH) nao entende
# \s como classe POSIX e devolveria 0 casamentos — o assert de 18 regras logo abaixo
# viraria falso alarme permanente numa maquina sem GNU grep.
echo "regras exportadas: $(grep -c '^[[:space:]]*- uid:' "$OUT/rules.yaml")"

# --- Scrub do segredo do Telegram -------------------------------------------------
#
# Medido ao vivo contra o Grafana da VPS: o export de contact points NAO devolve o
# bottoken em claro — devolve a string literal '[REDACTED]'. O risco aqui e o oposto
# do que se imaginaria, e igualmente silencioso: se '[REDACTED]' for parar no arquivo
# versionado, o `sed 's|${TELEGRAM_BOT_TOKEN}|...|'` do apply-cloud.sh nao acha nada
# para substituir (nao ha placeholder ali), o contact point e criado na nuvem com o
# token literal "[REDACTED]", e o Telegram simplesmente nunca entrega — sem erro
# nenhum na criacao. Por isso a escrita abaixo e uma ATRIBUICAO incondicional (nao um
# sub/replace condicional): cobre os dois casos possiveis, '[REDACTED]' de hoje e um
# token real em claro de outra versao do Grafana, sempre virando o placeholder que o
# apply-cloud.sh espera interpolar na hora de aplicar na nuvem.
#
# yq, nao sed: sed trocaria qualquer string parecida com token em qualquer lugar do
# arquivo (inclusive um chatid que por acaso tivesse o mesmo formato); yq escreve so no
# caminho certo da arvore.
yq -i '(.contactPoints[].receivers[] | select(.type == "telegram") | .settings.bottoken) = "${TELEGRAM_BOT_TOKEN}"' \
  "$OUT/contactpoints.yaml"

# chatid tem de sobreviver como STRING LITERAL. O crash loop documentado em
# infra/grafana/provisioning/alerting/contactpoints.yaml acontece quando o runtime de
# alerting re-avalia esse campo e encontra algo que "parece numero" (inclusive um
# numero YAML sem aspas, que e exatamente o que um export desavisado pode devolver).
# Forcar tostring garante que o valor sai sempre entre aspas, nao importa como chegou.
yq -i '(.contactPoints[].receivers[] | select(.type == "telegram") | .settings.chatid) |= (. | tostring)' \
  "$OUT/contactpoints.yaml"

# Rede de seguranca: aborta se sobrar no arquivo QUALQUER coisa com a cara de um bot
# token do Telegram (digitos, dois-pontos, 35+ chars). Se a substituicao acima falhar
# silenciosamente — caminho yq errado, schema do export mudou — isto e o unico freio
# entre o segredo e um `git add`.
if grep -qE '[0-9]+:[A-Za-z0-9_-]{35,}' "$OUT/contactpoints.yaml"; then
  echo "ABORTADO: sobrou algo com a cara de um bottoken do Telegram em $OUT/contactpoints.yaml — NAO commitar" >&2
  exit 1
fi

# Segundo lado da mesma moeda: se sobrou '[REDACTED]' em vez do placeholder, o
# apply-cloud.sh vai aplicar esse literal na nuvem e o Telegram fica mudo para
# sempre, sem nenhum erro visivel — falha tao silenciosa quanto vazar o segredo.
if grep -q '\[REDACTED\]' "$OUT/contactpoints.yaml"; then
  echo "ABORTADO: '[REDACTED]' ainda presente em $OUT/contactpoints.yaml — a substituicao pelo placeholder \${TELEGRAM_BOT_TOKEN} nao pegou" >&2
  exit 1
fi

# --- Parametrizacao dos UIDs de datasource ----------------------------------------
#
# Os UIDs locais (prometheus, loki) nao existem na nuvem. O uid PODE aparecer em DUAS
# posicoes por regra: datasourceUid no nivel de data[] e model.datasource.uid
# aninhado. Medido ao vivo contra o export real da API (nao so contra o provisioning
# manual do repo): a API normaliza as regras de Prometheus e elas ficam SO com
# datasourceUid — sem o model.datasource.uid aninhado. O aninhado so sobrevive nas 2
# regras de Loki (2 regras x 2 posicoes = 4). Por isso o numero esperado de
# __DS_PROM__ e 16 (uma posicao por regra, 16 regras Prometheus), nao 32 — mas a
# expressao yq que anda a arvore inteira continua obrigatoria, porque e exatamente a
# posicao aninhada das 2 regras Loki que um sed simples sobre "datasourceUid:" nao
# alcancaria.
#
# Nao usar sed aqui: um sed sobre "datasourceUid:" corrige so a primeira posicao e o
# grep -c de aceite passaria com o bug presente — falsa confianca exatamente onde
# doi (uma regra Loki apontando para um datasource que nao existe na nuvem falha
# calada). yq anda a arvore inteira e pega as duas.
yq -i '(.. | select(has("datasourceUid")).datasourceUid) |=
         (sub("^prometheus$", "__DS_PROM__") | sub("^loki$", "__DS_LOKI__")) |
       (.. | select(has("uid") and has("type")).uid) |=
         (sub("^prometheus$", "__DS_PROM__") | sub("^loki$", "__DS_LOKI__"))' \
  "$OUT/rules.yaml"

# --- Auto-verificacao (aborta, nao avisa) -----------------------------------------

qtd_regras=$(grep -c '^[[:space:]]*- uid:' "$OUT/rules.yaml" || true)
if [ "$qtd_regras" -ne 18 ]; then
  echo "ABORTADO: $qtd_regras regras em $OUT/rules.yaml, esperado 18 — export pegou escopo errado ou perdeu regra" >&2
  exit 1
fi

# grep -c dispara exit 1 quando NAO acha nada (o caso bom aqui) — sob set -e isso
# derrubaria o script antes de avaliarmos o resultado. `|| true` neutraliza so o
# grep, a decisao continua no `if` logo abaixo.
literal_restante=$(grep -nE '(datasourceUid|uid): (prometheus|loki)$' "$OUT/rules.yaml" || true)
if [ -n "$literal_restante" ]; then
  echo "ABORTADO: uid literal de datasource ainda presente em $OUT/rules.yaml (aponta para datasource que nao existe na nuvem):" >&2
  echo "$literal_restante" >&2
  exit 1
fi

qtd_prom=$(grep -c '__DS_PROM__' "$OUT/rules.yaml" || true)
qtd_loki=$(grep -c '__DS_LOKI__' "$OUT/rules.yaml" || true)
echo "placeholders observados: __DS_PROM__=$qtd_prom __DS_LOKI__=$qtd_loki (esperado 16 e 4, medido contra o export real da API; o numero exato depende do formato do export, o que importa e nao ser zero)"

# Um placeholder com contagem zero e o modo de falha silencioso desta fase: a
# substituicao "rodou" (yq nao deu erro) mas nao trocou nada, porque o caminho na
# arvore mudou ou o valor de origem nao era o esperado — e a regra segue apontando
# para um datasource que nao existe na nuvem, sem nenhum sinal de que algo quebrou.
if [ "$qtd_prom" -eq 0 ]; then
  echo "ABORTADO: __DS_PROM__ nao aparece em $OUT/rules.yaml — a parametrizacao nao substituiu nada" >&2
  exit 1
fi
if [ "$qtd_loki" -eq 0 ]; then
  echo "ABORTADO: __DS_LOKI__ nao aparece em $OUT/rules.yaml — a parametrizacao nao substituiu nada" >&2
  exit 1
fi

echo "export-local.sh OK: 18 regras, sem uid literal remanescente, sem bottoken em claro."
