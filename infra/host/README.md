# Configuração do host (VPS)

## daemon.json — teto de log dos containers Docker

Hoje o Docker no host usa o driver de log padrão `json-file` SEM limite de tamanho — os logs de container crescem indefinidamente até encher o disco. Este arquivo põe teto: cada container guarda no máximo 3 arquivos de 20 MB (60 MB por container).

### Instalação manual

1. Copiar para o host:
   ```bash
   sudo cp infra/host/daemon.json /etc/docker/daemon.json
   ```
   Se um `daemon.json` já existir no host, mesclar com preservação das outras chaves.

2. Reiniciar o Docker:
   ```bash
   sudo systemctl restart docker
   ```

### ⚠️ EFEITO COLATERAL (importante)

`systemctl restart docker` **DERRUBA todos os containers em execução**. Fazer isso na **janela do próximo deploy**, não em produção ao vivo aleatoriamente.

O teto de log só vale para containers **recriados após o restart** — o deploy seguinte (que recria os containers) já aplica naturalmente.

### Aplicado na VPS de produção

Conferido em 2026-08-09: `/etc/docker/daemon.json` já existe no host e é **idêntico** ao `infra/host/daemon.json` deste repositório — mesmo `json-file` com `max-size: 20m` e `max-file: 3`. Não é pendência; não rodar o `systemctl restart docker` do passo 2 achando que falta aplicar — ele derruba todos os containers em execução à toa.

## Swapfile — rede de segurança contra OOM no build de deploy

Aplicado na VPS de produção. O que existe lá hoje:

```bash
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo "/swapfile none swap sw 0 0" >> /etc/fstab          # persiste no boot
printf "vm.swappiness = 10\n" > /etc/sysctl.d/99-swappiness.conf
sysctl -p /etc/sysctl.d/99-swappiness.conf
```

Estado verificado depois de aplicar:
- `swapon --show` → `/swapfile file 2G 0B -2`
- `free -m` → `Swap: 2047 0 2047`
- `cat /proc/sys/vm/swappiness` → `10`

### Por quê

A VPS tem 1 vCPU e ~1967 MB de RAM e **não tinha swap nenhum**. Durante um `docker compose build` de deploy real, medido em 2026-08-09 amostrando a cada 5s, os dois `dotnet publish` (API e Web, que o compose builda em paralelo) somaram **pico de 610 MB de RSS**, a memória disponível do host caiu para **82 MB** e o load average chegou a **13,2** num único vCPU. Ou seja: o deploy de hoje passa a ~80 MB do OOM killer.

O swapfile é a rede para o build não ser morto no meio do deploy — **não** é para a aplicação rodar em swap. É por isso que `vm.swappiness=10`: o kernel só recorre ao swap sob pressão real de memória, em vez de trocar páginas para disco por hábito. Swap num disco de VPS é ordens de grandeza mais lento que RAM: se um container de aplicação (não o build) começar a swapar de forma sustentada, isso é sintoma a investigar, não capacidade nova.

Custo: 2 GB de disco.

Para reverter:
```bash
swapoff /swapfile
# remover a linha "/swapfile none swap sw 0 0" de /etc/fstab
rm /swapfile
```

## Limpeza de imagens antigas e cache de build (cron mensal sugerido)

Imagens Docker órfãs acumulam, mas isso é só metade do problema de espaço em disco.

```
0 4 1 * * docker image prune -af --filter "until=720h"
```

Executa todo dia 1º do mês às 04:00. Remove imagens não usadas com mais de 720 horas (30 dias):
- `-a`: inclui imagens sem container associado.
- `-f`: sem confirmação.

### `image prune` não é `builder prune`

`docker image prune` não toca no cache de build do BuildKit — são espaços de armazenamento distintos. Enquanto o deploy rodava `docker compose build --no-cache` (removido no mesmo PR que documentou isto), cada deploy **escrevia** entradas nesse cache que nunca seriam **lidas** de volta (o `--no-cache` garante isso).

Três medidas distintas, tomadas na limpeza:
- Antes: `df -h /` → `48G  39G  8.9G  82%` (dos quais 40 GB eram `/var/lib/docker`)
- `docker builder prune -af` → reportou `Total: 27.8GB` em 526 entradas
- Depois: `df -h /` → `48G  9.2G  39G  20%`

O disco caiu de 39 GB para 9,2 GB usados — uma liberação real de ~29,8 GB —, enquanto o `builder prune` contabilizou 27,8 GB. Os dois números não precisam fechar exatamente: a diferença de ~2 GB é o que o prune libera indiretamente, em camadas de imagem que só o cache de build referenciava.

Agora que o `--no-cache` saiu do deploy, esse mesmo cache deixa de ser lixo puro: ele é lido a cada build e torna o build mais barato (mais rápido, menos pico de CPU/memória — ver seção do swapfile acima). Por isso o cron complementar sugerido **não** usa `-a`, que apagaria justamente o cache quente:

```
0 4 1 * * docker builder prune -f --filter "until=720h"
```

Remove só entradas de cache de build não usadas há mais de 30 dias, preservando o cache recente.

## Métricas por container — textfile collector (74.6)

### O quê

`infra/host/container-metrics.sh` lê os arquivos de cgroup v2 de cada container em execução no host (`memory.current`, `memory.stat`, `memory.max`, `memory.peak`, `cpu.stat`, `memory.events`) mais o `RestartCount` do `docker inspect`, e escreve um arquivo `.prom` em `/var/lib/node_exporter/textfile/container_resources.prom`. O node-exporter, com `--collector.textfile` habilitado (`docker-compose.yml`, serviço `node-exporter`), serve esse arquivo como se fossem métricas suas próprias. Um systemd timer (`td-container-metrics.timer`) roda o script a cada 30s no host, casando com o `scrape_interval` do Prometheus (`prometheus.yml:10`).

Métricas emitidas, com label `container="<nome>"`:

| métrica | tipo | papel |
|---|---|---|
| `td_container_memory_unreclaimable_bytes` | gauge | **rege os alertas de memória** |
| `td_container_memory_reclaim_events_total` | counter | pressão real contra o teto |
| `td_container_memory_working_set_bytes` | gauge | diagnóstico (comparável ao cAdvisor) |
| `td_container_memory_limit_bytes` (ausente se o container não tem `memory.max`) | gauge | |
| `td_container_memory_peak_bytes` | gauge | |
| `td_container_cpu_cfs_periods_total` | counter | |
| `td_container_cpu_cfs_throttled_periods_total` | counter | |
| `td_container_oom_kill_total` | counter | best-effort (zera no restart) |
| `td_container_restarts_total` | counter | atravessa o restart |
| `td_container_up` | gauge | |

**Por que os alertas de memória NÃO usam `working_set`.** `working_set` (`memory.current − inactive_file`) é a definição do cAdvisor e inclui `active_file` — page cache **reclamável**, que o kernel joga fora sob pressão sem matar ninguém. Os tetos da 74.3 foram dimensionados a partir do **não-descartável** (`anon + shmem + kernel`), então alertar sobre `working_set` mediria uma coisa diferente da que o teto governa. Medido na VPS: `node-exporter` a 78% de `working_set` contra **45%** de não-descartável, `promtail` a 70% contra **42%** — um alerta de 85% sobre `working_set` acusaria risco de OOM onde só há cache.

`td_container_memory_reclaim_events_total` é o campo `max` de `memory.events`: quantas vezes uma alocação bateu no teto e forçou reclaim. Foi ele que acusou os 9131 eventos do `grafana` mal dimensionado na 74.3, e com os tetos corrigidos a linha de base medida é **zero nos oito containers** — o que dispensa limiar chutado.

Cobre TODOS os containers em execução no host — não só os do `docker-compose.yml` deste projeto (o container-isca do gate de deploy, por exemplo, não tem nome do projeto e ainda assim aparece).

### Por quê (e por que não cAdvisor)

`docs/PLANO.md` (74.6) decidiu cAdvisor pela medição, e a medição o rejeitou: ele dá exatamente as métricas certas, mas custa **60-120 MB de RSS** contra um orçamento de **~42 MB** no bloco INFRA compartilhada — 15-25% do bloco gasto só para monitorar o próprio bloco. A alternativa de custo quase zero é o textfile collector do node-exporter: o mesmo binário que já roda no bloco, alimentado por um script simples de host via systemd timer, sem container novo, sem sidecar, sem explosão de séries realimentando a memória do Prometheus.

O preço é dado mais grosso (leitura de cgroup direta, não a agregação fina do cAdvisor) e uma instalação manual de host — o mesmo trade-off já aceito para `daemon.json` e o swapfile acima.

### Instalação manual na VPS

```bash
sudo cp infra/host/container-metrics.sh /usr/local/bin/container-metrics.sh
sudo chmod +x /usr/local/bin/container-metrics.sh

sudo cp infra/host/td-container-metrics.service /etc/systemd/system/
sudo cp infra/host/td-container-metrics.timer /etc/systemd/system/

sudo mkdir -p /var/lib/node_exporter/textfile

sudo systemctl daemon-reload
sudo systemctl enable --now td-container-metrics.timer
```

O `docker compose up -d` seguinte (ou um `up -d node-exporter` isolado) precisa recriar o `node-exporter` para pegar o novo `--collector.textfile` e o mount de `/var/lib/node_exporter/textfile` — um `docker-compose.yml` mudo (sem tocar `src/`) não recria containers por si só (ver `feedback_deploy_docs_nao_recria`), então force manualmente se não houver deploy de código no mesmo commit:

```bash
docker compose up -d --force-recreate node-exporter
```

### Como verificar

```bash
sudo systemctl status td-container-metrics.service
systemctl list-timers td-container-metrics.timer

curl -s localhost:9100/metrics | grep td_container_
```

A ausência prolongada de atualização do arquivo (timer parado, script falhando) fica visível pela métrica padrão `node_textfile_mtime_seconds` do próprio node-exporter — é ela quem sustenta um alerta de obsolescência, não uma métrica nova deste script.

### ⚠️ Instalar o timer na MESMA janela do deploy

A regra `td-metricas-container-obsoletas` é o dead-man's switch de todo este mecanismo: sem ela, se o timer do host morrer, as cinco regras por container param de disparar **em silêncio**. Por isso ela usa `noDataState: Alerting` — e `node_textfile_mtime_seconds` **não existe** enquanto não houver nenhum `.prom` no diretório.

Consequência prática: entre o deploy do código e a instalação manual do timer, o Telegram recebe um alerta *"Métricas de container obsoletas"*. **Isso é esperado, não é defeito** — é o dead-man funcionando. Ele resolve sozinho no primeiro ciclo do timer (até 30s depois do `systemctl enable --now`). Fazer as duas coisas na mesma janela evita o ruído.

O diretório em si não é problema: o Docker **cria** o caminho do bind mount se ele não existir (verificado na VPS), então o `node-exporter` sobe normalmente com o diretório vazio — ele só não exporta nenhuma série `td_container_*` até o primeiro `.prom` aparecer.

### Como reverter

```bash
sudo systemctl disable --now td-container-metrics.timer
sudo rm /etc/systemd/system/td-container-metrics.service /etc/systemd/system/td-container-metrics.timer
sudo systemctl daemon-reload
sudo rm -rf /var/lib/node_exporter/textfile
sudo rm /usr/local/bin/container-metrics.sh
```

Depois, remover `--collector.textfile`/`--collector.textfile.directory` e o mount de `/var/lib/node_exporter/textfile` do serviço `node-exporter` em `docker-compose.yml` e recriar o container (`docker compose up -d --force-recreate node-exporter`) — sem isso ele fica com um coletor apontando para um diretório que não existe mais (inofensivo, mas sujo).
