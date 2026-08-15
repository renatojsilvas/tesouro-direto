# Grafana Alloy — o agente único de observabilidade

Desde a tarefa 77 este container é **toda** a observabilidade que roda na VPS. Ele substituiu
`grafana`, `loki`, `prometheus`, `promtail` e `node-exporter` (516 MB de teto somado). Grafana,
Prometheus e Loki agora vivem no **Grafana Cloud** (free tier), e o Alloy só coleta e empurra.

A razão principal não foi memória: com o alerting rodando *dentro* da VPS, a queda da VPS calava o
alerta junto. Na nuvem, ausência de dado vira `NoData` e o Telegram toca — dead-man's switch de
graça.

## O que o `config.alloy` faz

| bloco | o que coleta | para onde vai |
|---|---|---|
| `prometheus.scrape "app"` | `/metrics` da API (`job="tesouro-direto-api"`) | `prometheus.remote_write "cloud"` |
| `prometheus.exporter.unix "host"` + `discovery.relabel "node"` | métricas de host e o textfile collector de `infra/host/container-metrics.sh` (`job="node"`) | idem |
| `loki.source.file "nginx"` | access log do nginx, **com hash de IP** | `loki.write "cloud"` |
| `loki.source.file "kernel"` | `kern.log`, filtrado para linhas de OOM | idem |
| `loki.source.api "app"` | recebe o push do sink Serilog da API e do Web (`alloy:3100`) | idem |

O `discovery.relabel "node"` não é decoração: o `prometheus.exporter.unix` rotula com
`job="integrations/unix"` por padrão, e sem o relabel a identidade das séries de host muda.

### Hash de IP (LGPD, obrigatório)

O access log do nginx tem IP de usuário, e mandá-lo ao Grafana Labs é transferência internacional de
dado pessoal (Res. CD/ANPD 19/2024). O pipeline do nginx aplica SHA3-256 com salt (`GC_IP_SALT`)
**antes** do envio. O `topk by ip` continua funcionando — ele só precisa distinguir, não
identificar.

> `GC_IP_SALT` **não é uma credencial comum**: um token perdido se regenera; um salt perdido
> invalida a correlação histórica de IP e não se recupera.

## Variáveis de ambiente

Todas obrigatórias (`${VAR:?}` no `docker-compose.yml` — uma faltando derruba a interpolação do
compose **inteiro**, não só o serviço `alloy`). Os valores saem da página *Details* da stack no
Grafana Cloud:

| variável | onde obter |
|---|---|
| `GC_PROM_URL`, `GC_PROM_USER` | Details → Prometheus (URL de remote write e Instance ID) |
| `GC_LOKI_URL`, `GC_LOKI_USER` | Details → Loki (URL de push e Instance ID) |
| `GC_TOKEN` | Access Policy Token com escopos `metrics:write` e `logs:write` |
| `GC_IP_SALT` | gere com `openssl rand -hex 32` e **guarde** |

O token que provisiona alertas e dashboards na nuvem é **outro** (service account `glsa_`, role
Admin) e vive fora do container — ver `scripts/grafana-cloud/`.

## Limitações conhecidas

- **O dashboard do k6 não vive na nuvem — e não pode.** `infra/grafana/dashboards/load-test.json`
  lê do **Prometheus efêmero local** (`docker-compose.load.yml`, sobe só sob `--profile load`
  durante o teste de carga, publicado em `127.0.0.1:9090` com `--web.external-url=/prometheus/`).
  `scripts/grafana-cloud/apply-cloud.sh` **não** sobe esse dashboard (ver comentário no loop de
  dashboards do script) e apaga da nuvem, de forma idempotente, o `load-test-k6` que chegou a subir
  lá por engano na 77.3. A causa **não é rede** — um túnel SSH só dá acesso ao Prometheus efêmero a
  partir da MÁQUINA de quem abriu o túnel, nunca a partir do backend SaaS do Grafana Cloud, que não
  tem (e nunca terá) rota para o `127.0.0.1` de quem roda o teste. Qualquer painel salvo na nuvem
  apontando para esse datasource fica permanentemente vazio e calado — exatamente o modo de falha
  que a tarefa 77 existe para eliminar. Mandar `k6_*` para a nuvem para contornar isso também está
  **proibida** por decisão registrada: a cardinalidade é alta e transiente, e estourar o teto de
  séries durante um teste de carga perderia justamente as métricas do teste. O caminho que de fato
  funciona: `run-load.sh` imprime, ao final da execução, o destino real dos dados (o Prometheus
  local em `--local`, ou o `K6_PROMETHEUS_RW_SERVER_URL` explícito no outro modo) — consulte por lá,
  nunca no Grafana Cloud.
- **Queda de rede é assimétrica.** Métrica sobrevive (o `remote_write` tem WAL em disco); log é
  perdido (buffer em memória). Aceito e registrado.

## Operação

```bash
# validar a config antes de subir (rodar SEMPRE que editar o config.alloy)
docker run --rm -v "$PWD/infra/alloy:/c" grafana/alloy:latest fmt /c/config.alloy

# recriar depois de mudar a config — bind-mount NAO e recriado por `up -d` sozinho
docker compose up -d --force-recreate --no-deps alloy

# UI local do proprio Alloy (componentes, saude, ultimo erro de envio)
http://127.0.0.1:12345
```

Um `remote_write` com credencial errada falha em **silêncio** do ponto de vista da aplicação — o
lugar de olhar é a UI acima ou o log do container.
