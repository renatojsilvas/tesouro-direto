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

`docker image prune` não toca no cache de build do BuildKit — são espaços de armazenamento distintos. Enquanto o deploy rodava `docker compose build --no-cache` (removido no mesmo PR que documentou isto), cada deploy **escrevia** entradas nesse cache que nunca seriam **lidas** de volta (o `--no-cache` garante isso). O resultado, medido antes da limpeza: **27,8 GB em 526 entradas** de cache de build, com o disco da VPS em **82% de uso** (39 GB de 48 GB, dos quais 40 GB eram `/var/lib/docker`). Rodar `docker builder prune -af` liberou os 27,8 GB e derrubou o uso do disco para 20%.

Agora que o `--no-cache` saiu do deploy, esse mesmo cache deixa de ser lixo puro: ele é lido a cada build e torna o build mais barato (mais rápido, menos pico de CPU/memória — ver seção do swapfile acima). Por isso o cron complementar sugerido **não** usa `-a`, que apagaria justamente o cache quente:

```
0 4 1 * * docker builder prune -f --filter "until=720h"
```

Remove só entradas de cache de build não usadas há mais de 30 dias, preservando o cache recente.
