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

## Limpeza de imagens antigas (cron mensal sugerido)

Imagens Docker órfãs também acumulam. Adicionar ao crontab do host:

```
0 4 1 * * docker image prune -af --filter "until=720h"
```

Executa todo dia 1º do mês às 04:00. Remove imagens não usadas com mais de 720 horas (30 dias):
- `-a`: inclui imagens sem container associado.
- `-f`: sem confirmação.
