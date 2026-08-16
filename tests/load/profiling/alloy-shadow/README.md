# alloy-shadow — andaime de medição da fase 77.0

## O que é

Artefatos que rodam na VPS de produção, em `/opt/alloy-shadow`, para medir o
pico real de memória "não-descartável" (anon+shmem+kernel de `memory.stat`)
do Grafana Alloy **antes** de decidir a tarefa 77 (migração de observabilidade
para o Grafana Cloud). É a fase 77.0 do plano.

**Correção de fórmula (revisão adversarial do portão 77.0, 2026-08-15):**
até essa data, `fp-alloy-guard.sh` usava `anon+shmem+slab`, copiada por
engano de `/root/fp-gate76.sh` — fórmula que soma `slab` em dobro, pois no
cgroup v2 `slab` já está dentro de `kernel`. As amostras de
`/var/tmp/fp-alloy.csv` anteriores a essa correção usam a fórmula antiga e
por isso **subcontam a não-descartável em ~2%**; o CSV tem uma linha
marcadora (`# CORRECAO-FORMULA ...`) no ponto exato da troca. Até a 80.2,
`anon+shmem+kernel` era a fórmula canônica de `infra/host/container-metrics.sh`.

**Divergência proposital desde a 80.2 (revisão adversarial da 80.2,
2026-08-16):** `infra/host/container-metrics.sh` passou a usar
`anon+shmem+(kernel−slab_reclaimable)` — mais precisa, pois `slab_reclaimable`
volta ao sistema sob pressão sem OOM. Este andaime (`fp-alloy-guard.sh`) **não**
acompanha essa mudança: continua em `anon+shmem+kernel`, de propósito, para
preservar a comparabilidade com as medições históricas já registradas no
PLANO (74.x, 76, 77.x) e no `/var/tmp/fp-alloy.csv` desta fase. `anon+shmem+kernel`
deixou de ser "a fórmula canônica do projeto" na 80.2 — é a fórmula histórica
deste andaime, mantida por esse motivo. A fonte de verdade para produção é
sempre `infra/host/container-metrics.sh`.

Esta pasta versiona uma cópia fiel (hash idêntico ao arquivo em produção) de:

- `docker-compose.yml` — sobe o container `tesouro-direto-alloy` sozinho,
  sem `deploy.resources.limits`/`mem_limit`, exatamente para não mascarar o
  pico real.
- `fp-alloy-guard.sh` — roda via cron a cada 5 min, calcula a não-descartável
  do container `tesouro-direto-alloy` com a fórmula histórica deste andaime
  (`anon+shmem+kernel`, ver nota de divergência acima) e grava em
  `/var/tmp/fp-alloy.csv`. Tem um corte de segurança em 500 MB (que só evita
  OOM do host — não é o teto de decisão da 77.0, que é avaliado à parte na
  tabela de decisão do runbook, olhando o CSV).

O `config.alloy` usado pelo `docker-compose.yml` **não** está duplicado
aqui: é o mesmo arquivo já versionado em `infra/alloy/config.alloy`.

## Por que fica fora do compose de produção e fora do deploy

`/opt/alloy-shadow` é propositalmente isolado de `/opt/tesouro-direto`
(stack de produção). Isso garante duas coisas:

1. O próximo deploy de `/opt/tesouro-direto` não é afetado por este
   experimento — nada aqui entra no `docker-compose.yml` de produção nem no
   fluxo de deploy.
2. A medição pode existir e evoluir na VPS **sem exigir merge/push** antes
   dela estar pronta — só depois de medido é que a decisão da 77 (entra ou
   não entra o Alloy na stack) volta como código versionado de verdade.

## Como reproduzir do zero

Na VPS:

```bash
mkdir -p /opt/alloy-shadow
cp infra/alloy/config.alloy /opt/alloy-shadow/config.alloy
cp tests/load/profiling/alloy-shadow/docker-compose.yml /opt/alloy-shadow/docker-compose.yml

# .env com as 6 variáveis exigidas pelo docker-compose.yml (env_file: .env).
# NUNCA commitar este arquivo — contém segredo. Valores vêm do stack do
# Grafana Cloud (Connections > Prometheus/Loki > Remote write / Access Policy):
#   GC_PROM_URL  — endpoint remote_write do Prometheus do Grafana Cloud
#   GC_PROM_USER — username/instance ID do Prometheus do Grafana Cloud
#   GC_TOKEN     — API token (access policy) com escopo metrics:write,logs:write
#   GC_LOKI_URL  — endpoint de push do Loki do Grafana Cloud (77.2)
#   GC_LOKI_USER — username/instance ID do Loki do Grafana Cloud (77.2)
#   GC_IP_SALT   — salt do hash de IP (LGPD), `openssl rand -hex 32` (77.2)
cat > /opt/alloy-shadow/.env <<'EOF'
GC_PROM_URL=...
GC_PROM_USER=...
GC_TOKEN=...
GC_LOKI_URL=...
GC_LOKI_USER=...
GC_IP_SALT=...
EOF

cd /opt/alloy-shadow && docker compose up -d
```

## Como instalar o guard

```bash
cp tests/load/profiling/alloy-shadow/fp-alloy-guard.sh /root/fp-alloy-guard.sh
chmod +x /root/fp-alloy-guard.sh
```

Adicionar ao crontab do root a linha:

```
*/5 * * * * /root/fp-alloy-guard.sh >/dev/null 2>&1
```

**Aviso**: edite o crontab com `crontab -e` e apenas ACRESCENTE a linha
acima. Não use `crontab arquivo-novo` nem qualquer operação que sobrescreva
o crontab inteiro — ele já tem outras entradas ativas (ex.: o
`/root/fp-gate76.sh` da tarefa 76, que está fazendo uma medição em curso).
Apagar essa entrada por acidente destrói uma medição em andamento.

## Limpeza obrigatória ao fechar a fase 77.0

Isto é andaime, não infraestrutura permanente. As tarefas 74.3 e 76 já
cometeram o mesmo tipo de artefato só-na-VPS e a remoção deles ficou como
pendência aberta (o `/root/fp-gate76.sh` ainda está rodando na VPS até
hoje). Aqui, a limpeza abaixo é **parte do critério de aceite** da fase
77.0 — a fase não está fechada enquanto ela não rodar:

```bash
# 1. Remove a linha do cron (preserva as demais entradas)
crontab -l | grep -v fp-alloy-guard | crontab -

# 2. Derruba o stack shadow e o volume
docker compose -f /opt/alloy-shadow/docker-compose.yml down -v

# 3. Remove os artefatos da VPS
rm -rf /opt/alloy-shadow
rm -f /root/fp-alloy-guard.sh
```

## Sobre o `.env`

`/opt/alloy-shadow/.env` contém segredo (token de escrita do Grafana Cloud)
e **nunca** deve ser copiado para este repositório. Esta cópia versionada
inclui apenas `docker-compose.yml` e `fp-alloy-guard.sh` — nenhum valor de
credencial.
