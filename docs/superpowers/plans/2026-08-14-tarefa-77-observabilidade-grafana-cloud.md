# Tarefa 77 — Observabilidade para o Grafana Cloud via Alloy

> **Para executores agênticos:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development`
> (recomendado) ou `superpowers:executing-plans` para executar este plano fase a fase. Os passos usam
> checkbox (`- [ ]`) para rastreamento.

**Goal:** Substituir os 5 containers de observabilidade da VPS (`grafana`, `loki`, `prometheus`,
`promtail`, `node-exporter`) por um único `alloy` que faz scrape/tail local e envia para o free tier
do Grafana Cloud, devolvendo memória à VPS e tirando o alerting de dentro da máquina que ele vigia.

**Architecture:** Alloy roda como container único na rede `tesouro-net`. Ele (a) faz `prometheus.scrape`
do `/metrics` do app e do `prometheus.exporter.unix` embutido, (b) faz `loki.source.file` do
`/var/log/nginx/*access.log` e do `kern.log`, (c) expõe `loki.source.api` na porta 3100 para receber o
push do Serilog da API e do Web sem que eles precisem de credencial, e (d) faz `remote_write`/`loki.write`
autenticados para o Grafana Cloud. Dashboards e as 18 regras de alerta são exportados do Grafana local
pela API de provisioning e recriados na nuvem por script versionado — sem Terraform.

**Tech Stack:** Grafana Alloy (container), Grafana Cloud free tier, docker compose, GitHub Actions,
bash + curl + jq para a migração de alerting, k6 (inalterado).

---

## Global Constraints

Valores exatos, copiados das fontes. Todas as fases herdam esta seção.

- **Free tier do Grafana Cloud:** 10.000 séries ativas · 50 GB de log/mês · **retenção 14 dias** ·
  3 usuários · **500 regras de alerta** · **máximo 10 versões armazenadas por regra**.
  Estourar **rejeita ingestão, não gera fatura**.
- **DPM:** o uso cobrado é `max(séries ativas, DPM total ÷ DPM incluído por série)`, com 1 DPM incluído
  = scrape a cada 60s. O `scrape_interval` atual é **30s** (`prometheus.yml:10`) ⇒ multiplicador **2×**.
  **NÃO mudar para 60s:** a regra `td-db-readiness-down` tem `for: 1m` e ficaria com uma única amostra
  na janela (o próprio `prometheus.yml:2-9` registra essa rejeição).
- **Nomes de `job` são contrato com as 18 regras.** Preservar exatamente: `tesouro-direto-api` (12
  regras), `node`, `nginx`, `kernel`, `tesouro-direto-web`. Qualquer renomeação deixa alertas mudos
  **em silêncio**.
- **Métrica de dimensionamento é a NÃO-DESCARTÁVEL** (`td_container_memory_unreclaimable_bytes`),
  nunca `memory.current` nem `working_set`. Vale para medir o Alloy e para fixar o teto dele.
- **LGPD:** IP de usuário não sai da VPS em claro. Hash com salt no pipeline do Alloy **antes** do envio.
- **Não enviar métricas `k6_*` para a nuvem** — cardinalidade alta e transiente; estourar o teto durante
  um teste de carga faz perder justamente as métricas do teste.
- **Orçamento da VPS:** 1 vCPU / 1967 MB. O estado atual pós-tarefa 76 é 1060 MB (53,9%). A tarefa 77
  só se justifica se **reduzir** esse número.
- Um **PR por fase**. Suíte inteira verde em cada PR (baseline atual: **841/841**).
- `docker compose config -q` tem de passar nas 3 combinações do gate do CI
  (`.github/workflows/deploy.yml:59-72`): base, `+profiling`, `+load --profile load`.

---

## Inventário de partida (medido, não estimado)

O que sai, com o teto atual de cada um (`docker-compose.yml`):

| serviço | linhas | memória (teto) | o que faz que precisa sobreviver |
|---|---|---|---|
| `grafana` | 205-245 | 224M | 3 dashboards, 18 regras, contact point Telegram |
| `loki` | 247-283 | 160M | armazena log de 4 fontes, retenção 7d |
| `prometheus` | 285-320 | 72M | 3 scrape jobs, retenção 7d/256MB |
| `promtail` | 396-436 | 36M | tail de nginx + kern.log |
| `node-exporter` | 322-394 | 24M | métricas de host + **textfile collector** |
| **total** | ~232 linhas | **516M** | |

**Quatro fontes de log, não duas.** O promtail cobre só `nginx` e `kernel`. API e Web empurram
**direto** para `http://loki:3100` pelo sink do Serilog (`src/TesouroDireto.API/Extensions/SerilogExtensions.cs:20-23`
e `src/TesouroDireto.Web/Extensions/SerilogExtensions.cs:20-23`, env `Loki__Uri` em
`docker-compose.yml:11` e `:166`). Remover o container `loki` sem redirecionar o Serilog faz o log de
aplicação **sumir em silêncio** — o sink tem buffer/retry, então não há crash, só ausência.

**Chave estrangeira dos UIDs.** Os datasources têm `uid: prometheus` e `uid: loki` fixos
(`infra/grafana/provisioning/datasources/datasources.yml`). Na nuvem os UIDs são outros. Isso é
referenciado por: 18 regras (16 em `prometheus`, 2 em `loki`) e **59** refs nos 3 dashboards
(`tesouro-direto.json` 16 prom + 13 loki, `host.json` 12 prom, `load-test.json` 18 prom).

**Armadilha do UID nas 2 regras Loki:** elas carregam o uid em **dois** lugares —
`datasourceUid: loki` no nível de `data[]` **e** `model.datasource.uid: loki` aninhado
(`rules.yaml:326` e `:674`). Um `sed` sobre `datasourceUid:` corrige o primeiro e deixa o segundo
apontando para um datasource que não existe na nuvem. Por isso a fase 77.3 usa `yq`, que anda a
árvore, e não `sed`.

**Sete regras dependem do textfile collector** (`td-container-*` + `td-metricas-container-obsoletas`),
que escreve em `/var/lib/node_exporter/textfile/container_resources.prom` e só é lido pelo
`node-exporter` (`--collector.textfile.directory=/host/textfile`).

**Zero cobertura de teste no provisioning.** Nenhum teste faz parse de `rules.yaml`,
`datasources.yml`, `contactpoints.yaml` ou dos dashboards. Eles sumiriam sem nenhum sinal vermelho.
A única validação automatizada vive fora da suíte, em dois comandos ruby allowlistados em
`.claude/settings.local.json:61,66`, que abortam se `chatid != "144442958"`.

---

## Estrutura de arquivos

**Criar:**
- `infra/alloy/config.alloy` — configuração única do Alloy (scrape, tail, process, write)
- `infra/alloy/README.md` — o que roda onde, como verificar, como fazer rollback
- `scripts/grafana-cloud/export-local.sh` — exporta regras/contact points/policies do Grafana local
- `scripts/grafana-cloud/apply-cloud.sh` — recria na nuvem via API de provisioning, com UIDs corrigidos
- `scripts/grafana-cloud/lib.sh` — resolução de UID de datasource, `curl` com auth, idempotência
- `infra/grafana/cloud/rules.yaml` — regras exportadas, com UIDs parametrizados (fonte da verdade)
- `infra/grafana/cloud/contactpoints.yaml`, `infra/grafana/cloud/policies.yaml`
- `tests/TesouroDireto.API.Tests/Observability/AlloyContractTests.cs` — trava os nomes de `job` e de
  métrica que as regras consomem

**Modificar:**
- `docker-compose.yml:205-437` — remove 5 serviços, adiciona `alloy`, move `prometheus` para
  `profiles: ["load"]`
- `docker-compose.yml:11,166` — `Loki__Uri` passa a apontar para o Alloy
- `docker-compose.load.yml` — passa a depender do `prometheus` com profile
- `infra/nginx/tesouro-direto.conf:75-83,103-108,166-174,194-199` — remove as rotas mortas
- `.github/workflows/deploy.yml:63-65,167-176,214` — secrets e a lista de serviços recriados
- `run-load.sh:70-75,107` — `--profile load` e URL do dashboard
- `prometheus.yml` — vira arquivo só de teste de carga (perde os jobs `node` e `prometheus`)
- `.env.example` — sai `GRAFANA_PASSWORD`/`GRAFANA_ROOT_URL`, entram as 6 `GC_*`
- Docs: `README.md`, `docs/MAPA.md`, `docs/analises/observabilidade.md`, `infra/host/README.md`

**Deletar (só na fase 77.4, depois que a nuvem estiver provada):**
- `infra/loki/loki-config.yml`, `infra/promtail/promtail-config.yml`
- `infra/grafana/provisioning/**` (após exportado para `infra/grafana/cloud/`)

---

## Fase 77.0 — Medir o Alloy antes de decidir (PORTÃO)

**Por que esta fase existe primeiro.** A decisão de 11/08 estimou o Alloy em **130–164 MB**. Essa
estimativa não foi medida, e os relatos públicos são muito piores: há registros de 400 MB com volume
de log de 0,005 MB/s e de 742 MiB num deployment pequeno, com usuários relatando que o Alloy consome
**significativamente mais** CPU e memória que o promtail no mesmo workload. Se o Alloy custar 400 MB,
a economia cai de ~456 MB para ~116 MB e o custo/benefício da tarefa inteira muda. O princípio do
dono é **medir antes de fixar** — esta fase aplica ele à premissa central da tarefa.

**Nada é removido nesta fase.** O Alloy sobe *ao lado* da stack atual, escrevendo para a nuvem em
paralelo. Se a medição reprovar, o rollback é `docker compose stop alloy` e a tarefa 77 vira "não
fazer", com o número medido registrado.

**Files:**
- Create: `infra/alloy/config.alloy`
- Modify: `docker-compose.yml` (adicionar serviço `alloy`, sem remover nada)
- Create: `scratchpad/medir-alloy.md` (registro da medição)

**Interfaces:**
- Produces: número medido de `td_container_memory_unreclaimable_bytes{container="tesouro-direto-alloy"}`
  em pico de 24h sob carga, que vira o `deploy.resources.limits.memory` da fase 77.5.

- [ ] **Passo 1: criar conta e stack no Grafana Cloud (manual, do dono)**

Em https://grafana.com/auth/sign-up/create-user — free tier, sem cartão. Anotar da página
"Details" da stack os 4 valores:
- URL do Prometheus remote_write (formato `https://prometheus-prod-XX-prod-YY-Z.grafana.net/api/prom/push`)
- Username/Instance ID do Prometheus (numérico)
- URL do Loki push (formato `https://logs-prod-XX.grafana.net/loki/api/v1/push`)
- Username/Instance ID do Loki (numérico)

Gerar um **Access Policy Token** com escopos `metrics:write`, `logs:write`. Guardar em
`scratchpad/gc_token` (o `scratchpad/` está no `.gitignore:20`).

- [ ] **Passo 2: escrever a config mínima do Alloy — só métricas do app**

Criar `infra/alloy/config.alloy`:

```alloy
// Tarefa 77 — coletor único que substitui prometheus/loki/promtail/node-exporter/grafana.
// Os nomes de `job` abaixo são CONTRATO com as 18 regras de alerta: mudar um deixa
// alertas mudos em silêncio. Ver docs/superpowers/plans/2026-08-14-tarefa-77-*.md.

prometheus.remote_write "cloud" {
  endpoint {
    url = sys.env("GC_PROM_URL")
    basic_auth {
      username = sys.env("GC_PROM_USER")
      password = sys.env("GC_TOKEN")
    }
  }
}

// job="tesouro-direto-api" — consumido por 12 das 18 regras.
prometheus.scrape "app" {
  targets         = [{ __address__ = "app:8080", job = "tesouro-direto-api" }]
  metrics_path    = "/metrics"
  scrape_interval = "30s"   // NÃO subir para 60s: quebra o `for: 1m` de td-db-readiness-down
  forward_to      = [prometheus.remote_write.cloud.receiver]
}
```

- [ ] **Passo 3: adicionar o serviço `alloy` ao compose, SEM remover nada**

Em `docker-compose.yml`, antes do bloco `volumes:` (linha 438):

```yaml
  alloy:
    image: grafana/alloy:v1.12.0
    container_name: tesouro-direto-alloy
    restart: unless-stopped
    command:
      - run
      - --server.http.listen-addr=0.0.0.0:12345
      - --storage.path=/var/lib/alloy/data
      - /etc/alloy/config.alloy
    environment:
      - GC_PROM_URL=${GC_PROM_URL:?defina GC_PROM_URL no .env}
      - GC_PROM_USER=${GC_PROM_USER:?defina GC_PROM_USER no .env}
      - GC_TOKEN=${GC_TOKEN:?defina GC_TOKEN no .env}
    ports:
      - "127.0.0.1:12345:12345"
    volumes:
      - ./infra/alloy/config.alloy:/etc/alloy/config.alloy:ro
      - alloy-data:/var/lib/alloy/data
    # SEM deploy.resources.limits nesta fase: o objetivo é medir o pico REAL, não
    # observar o container bater num teto que eu escolhi antes de saber o número.
```

E em `volumes:` (linha 438-443) acrescentar `alloy-data:`.

- [ ] **Passo 3b: validar a sintaxe do `config.alloy` LOCALMENTE, antes de ir para a VPS**

`docker compose config -q` valida o YAML do compose, **não** a DSL do Alloy. Sem este passo, a
primeira validação real da config aconteceria já rodando na VPS. Rodar sempre que o arquivo mudar
(fases 77.0, 77.1 e 77.2):

```bash
docker run --rm -v "$(pwd)/infra/alloy:/etc/alloy:ro" grafana/alloy:v1.12.0 \
  fmt /etc/alloy/config.alloy > /dev/null && echo "config.alloy OK"
```
Esperado: `config.alloy OK`. Erro de parse imprime linha e coluna — corrigir antes de seguir.

- [ ] **Passo 4: validar que o compose ainda parseia nas 3 combinações do gate do CI**

```bash
cd /Users/renatojsilva-dev/workspace/tesouro-direto-api
GC_PROM_URL=http://dummy GC_PROM_USER=1 GC_TOKEN=dummy \
GRAFANA_PASSWORD=dummy TELEGRAM_BOT_TOKEN=dummy \
K6_PROMETHEUS_RW_SERVER_URL=http://dummy:9090/prometheus/api/v1/write \
DB_PASSWORD=dummy API_KEY=dummy \
  docker compose config -q && echo "base OK"
```
Esperado: `base OK`, sem stderr.

- [ ] **Passo 5: subir na VPS ao lado da stack atual e confirmar ingestão**

```bash
ssh root@157.230.148.98 'cd /opt/tesouro-direto && docker compose up -d alloy && sleep 30 && docker logs --tail 40 tesouro-direto-alloy'
```
Esperado: sem `level=error`; a UI em `http://127.0.0.1:12345` (via túnel) mostra
`prometheus.scrape.app` com `Health: healthy`.

Confirmar do lado da nuvem, no Explore do Grafana Cloud:
```promql
up{job="tesouro-direto-api"}
```
Esperado: série presente, valor 1.

- [ ] **Passo 6: contar séries ativas reais contra o teto de 10k**

No Explore do Grafana Cloud:
```promql
count({__name__=~".+"})
```
Registrar o número. **Multiplicar por 2** (DPM de 30s) para obter o consumo cobrado.
Critério: `contagem × 2 < 8000` (margem de 20% sobre 10k) — senão, a fase 77.1 precisa de
`metric_relabel_config` para podar séries antes de qualquer outra coisa.

- [ ] **Passo 7: armar a medição de 24h da não-descartável do Alloy**

O textfile collector (`infra/host/container-metrics.sh`) já enumera **todos** os containers do host
(`docker ps --no-trunc`), então o `alloy` já está sendo medido sem nenhuma mudança. Consultar direto:

```promql
max_over_time(td_container_memory_unreclaimable_bytes{container="tesouro-direto-alloy"}[24h]) / 1024 / 1024
```

Deixar rodar **24h**, atravessando pelo menos uma janela de carga (`run-load.sh`). Registrar em
`scratchpad/medir-alloy.md`: pico da não-descartável, `memory.current` ao lado só para comparação,
e CPU (`td_container_cpu_cfs_throttled_periods_total`).

- [ ] **Passo 8: PORTÃO — decidir com o número na mão**

Ainda **sem** os pipelines de log e de host, que só aumentam o consumo. Critério de decisão, contra
os 516 MB que os 5 containers custam hoje:

| pico medido (não-descartável) | veredito |
|---|---|
| < 200 MB | **segue** — economia ≥ 300 MB, tarefa se paga |
| 200–350 MB | **segue com escopo reduzido** — avaliar manter `prometheus` local e migrar só log+alerting |
| > 350 MB | **para** — registrar o número na memória e fechar a 77 como "medida e rejeitada", igual à 74.2b |

Este passo **não é automatizável e não deve ser pulado**. Levar o número ao dono antes de abrir a 77.1.

- [ ] **Passo 9: commit**

```bash
git add infra/alloy/config.alloy docker-compose.yml
git commit -m "feat(77.0): alloy em shadow mode para medir footprint antes de migrar

Sobe o Alloy ao lado da stack atual, sem remover nada e sem teto de memoria,
para medir o pico real da nao-descartavel em 24h. A estimativa de 130-164 MB
da decisao de 11/08 nunca foi medida e os relatos publicos vao a 400-742 MB.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Fase 77.1 — Alloy assume as métricas de host e o textfile collector

**Pré-requisito:** portão da 77.0 aprovado.

**Files:**
- Modify: `infra/alloy/config.alloy`
- Modify: `docker-compose.yml` (volumes de host no `alloy`)
- Test: `tests/TesouroDireto.API.Tests/Observability/AlloyContractTests.cs` (criar)

**Interfaces:**
- Consumes: `prometheus.remote_write.cloud.receiver` da 77.0.
- Produces: séries `node_*` e `td_container_*` na nuvem com `job="node"`, que as 7 regras de
  container e a `td-disco-cheio` consomem.

- [ ] **Passo 1: escrever o teste que trava o contrato de nomes**

Criar `tests/TesouroDireto.API.Tests/Observability/AlloyContractTests.cs`:

```csharp
namespace TesouroDireto.API.Tests.Observability;

/// <summary>
/// As 18 regras de alerta filtram por nomes de `job` e de métrica que vivem fora do C#
/// (config.alloy, container-metrics.sh). Nada na suíte protegia esses nomes — a tarefa 77
/// descobriu que renomear um `job` deixa alertas mudos em silêncio. Estes testes falham
/// se alguém mexer num nome sem mexer nas regras.
/// </summary>
public class AlloyContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root não encontrado");
    }

    // Os casos abaixo cobrem SÓ os jobs que existem ao fim desta fase. `nginx` e `kernel`
    // entram na 77.2 e `TodoJobUsadoNasRegras` na 77.3, junto com os fatos que verificam —
    // teste que nasce vermelho e atravessa PR viola o Global Constraint e a regra
    // never_skip_failing_tests do projeto.
    [Theory]
    [InlineData("tesouro-direto-api")]
    [InlineData("node")]
    public void ConfigAlloy_DeclaraOsJobsQueAsRegrasConsomem(string job)
    {
        var config = File.ReadAllText(Path.Combine(RepoRoot(), "infra/alloy/config.alloy"));
        Assert.Contains($"\"{job}\"", config);
    }

    [Theory]
    [InlineData("td_container_memory_unreclaimable_bytes")]
    [InlineData("td_container_memory_limit_bytes")]
    [InlineData("td_container_oom_kill_total")]
    [InlineData("td_container_restarts_total")]
    [InlineData("td_container_memory_reclaim_events_total")]
    [InlineData("td_container_cfs_throttled_periods_total")]
    public void TextfileCollector_AindaEmiteAsMetricasQueAsRegrasConsomem(string metrica)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "infra/host/container-metrics.sh"));
        Assert.Contains(metrica, script);
    }
}
```

- [ ] **Passo 2: rodar e verificar que falha pelo motivo certo**

```bash
cd /Users/renatojsilva-dev/workspace/tesouro-direto-api
dotnet test tests/TesouroDireto.API.Tests --filter "FullyQualifiedName~AlloyContractTests" -v minimal
```
Esperado: FALHA em `ConfigAlloy_DeclaraOsJobsQueAsRegrasConsomem` para o caso `node` — ele ainda não
existe no `config.alloy`, que até aqui só tem o scrape do app da 77.0.

Confirmar que o caso `tesouro-direto-api` **passa**. Se ele também falhar, o teste está lendo o
arquivo errado — não é o config que está incompleto.

Nota sobre `td_container_cfs_throttled_periods_total`: o nome real no script é
`td_container_cpu_cfs_throttled_periods_total`. Este `InlineData` está **deliberadamente errado**
para provar que o teste detecta divergência; corrigir para o nome real no Passo 4 e observar o
teste virar verde. Se ele passar com o nome errado, o assert não está fazendo nada.

- [ ] **Passo 3: acrescentar o exporter unix ao config.alloy**

Acrescentar a `infra/alloy/config.alloy`:

```alloy
// Substitui o container node-exporter. A lista de coletores é a mesma de
// docker-compose.yml:339-375 — inclusive o textfile, que sustenta 7 regras de alerta.
prometheus.exporter.unix "host" {
  procfs_path = "/host/proc"
  sysfs_path  = "/host/sys"
  rootfs_path = "/rootfs"

  set_collectors = [
    "cpu", "meminfo", "loadavg", "filesystem", "diskstats",
    "netdev", "stat", "uname", "time", "os", "textfile",
  ]

  filesystem {
    mount_points_exclude = "^/(sys|proc|dev|host|etc)($|/)"
  }

  // Sem isso, cada deploy cria veth* novas = lote novo de séries ativas contra o teto de 10k.
  netdev {
    device_exclude = "^(veth.*|docker.*|br-.*)$"
  }

  textfile {
    directory = "/host/textfile"
  }
}

// job="node" — consumido por td-disco-cheio e pelas 7 regras td-container-*.
// O exporter.unix rotula os alvos com um job próprio; discovery.relabel força o nome do contrato.
discovery.relabel "node" {
  targets = prometheus.exporter.unix.host.targets
  rule {
    target_label = "job"
    replacement  = "node"
  }
}

prometheus.scrape "node" {
  targets         = discovery.relabel.node.output
  scrape_interval = "30s"
  forward_to      = [prometheus.remote_write.cloud.receiver]
}
```

- [ ] **Passo 4: corrigir o InlineData plantado no Passo 2**

Em `AlloyContractTests.cs`, trocar `td_container_cfs_throttled_periods_total` por
`td_container_cpu_cfs_throttled_periods_total`.

- [ ] **Passo 5: dar ao Alloy os mesmos binds de host que o node-exporter tinha**

Em `docker-compose.yml`, no serviço `alloy`, acrescentar aos `volumes` (os 4 vêm de
`docker-compose.yml:377-380`, todos read-only):

```yaml
      - /proc:/host/proc:ro
      - /sys:/host/sys:ro
      - /:/rootfs:ro
      - /var/lib/node_exporter/textfile:/host/textfile:ro
```

- [ ] **Passo 6: rodar os testes de contrato**

```bash
dotnet test tests/TesouroDireto.API.Tests --filter "FullyQualifiedName~AlloyContractTests" -v minimal
```
Esperado: **PASS em todos**. Nenhum teste desta classe pode ficar vermelho ao fim da fase — os casos
`nginx`/`kernel` e `TodoJobUsadoNasRegras` só são acrescentados quando os fatos que eles verificam
existirem (77.2 e 77.3).

- [ ] **Passo 7: verificar na VPS que o job saiu com o nome do contrato**

Este é o passo que a config sozinha não garante — `prometheus.exporter.unix` rotula os alvos com
`job` próprio e o `discovery.relabel` acima é a correção. Provar, não assumir:

```bash
ssh root@157.230.148.98 'cd /opt/tesouro-direto && docker compose up -d alloy'
```
No Explore do Grafana Cloud:
```promql
count by (job) ({__name__=~"node_.+"})
```
Esperado: **uma única** linha, `job="node"`. Se aparecer `integrations/unix` ou qualquer outro nome,
o `discovery.relabel` não pegou — ajustar antes de seguir.

E as métricas do textfile:
```promql
count by (job, container) (td_container_memory_unreclaimable_bytes)
```
Esperado: `job="node"` e um `container` por container do host (8 hoje, 9 com o alloy).

- [ ] **Passo 8: rodar a suíte inteira**

```bash
dotnet test -v minimal
```
Esperado: 841 + os novos de `AlloyContractTests`, **todos verdes**.

- [ ] **Passo 9: commit**

```bash
git add infra/alloy/config.alloy docker-compose.yml tests/TesouroDireto.API.Tests/Observability/AlloyContractTests.cs
git commit -m "feat(77.1): alloy assume metricas de host e textfile collector

prometheus.exporter.unix com a mesma lista de coletores do node-exporter,
incluindo textfile (sustenta 7 regras td-container-*). discovery.relabel
forca job=node porque o exporter rotula com nome proprio.

Novo AlloyContractTests trava os nomes de job e de metrica que as regras
consomem — nada na suite protegia isso.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Fase 77.2 — Alloy assume os 4 fluxos de log (com hash de IP)

**Files:**
- Modify: `infra/alloy/config.alloy`
- Modify: `docker-compose.yml:11,166` (`Loki__Uri`) e volumes de log no `alloy`
- Modify: `tests/.../AlloyContractTests.cs` (acrescentar os casos `nginx` e `kernel`)
- Modify: `infra/grafana/dashboards/tesouro-direto.json` (painel "Top IPs")

**Interfaces:**
- Consumes: `loki.write.cloud.receiver` (criado aqui).
- Produces: streams `job="nginx"` (labels `status`, `method`), `job="kernel"`,
  `job="tesouro-direto-api"`, `job="tesouro-direto-web"` na nuvem.

- [ ] **Passo 1: gerar o salt do hash de IP e colocá-lo no .env**

```bash
openssl rand -hex 32 > scratchpad/gc_ip_salt
cat scratchpad/gc_ip_salt
```
O salt **não vai para o repositório**. Ele entra como secret do GitHub (`GC_IP_SALT`) na fase 77.5 e
no `.env` da VPS. Trocar o salt invalida a correlação histórica de IP — é aceitável e desejável.

- [ ] **Passo 2: acrescentar os pipelines de log ao config.alloy**

```alloy
loki.write "cloud" {
  endpoint {
    url = sys.env("GC_LOKI_URL")
    basic_auth {
      username = sys.env("GC_LOKI_USER")
      password = sys.env("GC_TOKEN")
    }
  }
}

// ---------- nginx ----------
local.file_match "nginx" {
  path_targets = [{ __path__ = "/var/log/nginx/*access.log", job = "nginx" }]
}

loki.source.file "nginx" {
  targets    = local.file_match.nginx.targets
  forward_to = [loki.process.nginx.receiver]
}

loki.process "nginx" {
  // Mesmo regex do combined log format de infra/promtail/promtail-config.yml:20-21.
  stage.regex {
    expression = `^(?P<remote_addr>[\w\.]+) - (?P<remote_user>[^ ]*) \[(?P<time_local>.*)\] "(?P<method>[^ ]*) (?P<request_uri>[^ ]*) (?P<protocol>[^"]*)" (?P<status>[\d]+) (?P<body_bytes_sent>[\d]+) "(?P<referer>[^"]*)" "(?P<user_agent>[^"]*)"`
  }

  // LGPD: o IP é dado pessoal e a transferencia para o Grafana Labs e internacional
  // (Res. CD/ANPD 19/2024). Hash com salt ANTES do envio. Hash = SHA3-256 e exige salt.
  // O topk by ip do dashboard continua funcionando: ele so precisa DISTINGUIR clientes.
  // O salt é interpolado no NÍVEL DO RIVER (sys.env), não dentro do template Go — não
  // depender de `env` existir no escopo do template evita descobrir isso só em produção.
  stage.replace {
    expression = `^(\d+\.\d+\.\d+\.\d+)`
    replace    = "{{ .Value | Hash \"" + sys.env("GC_IP_SALT") + "\" }}"
  }

  // Só status e method viram label — igual ao promtail. Promover o IP a label seria
  // cardinalidade aberta pelo usuário.
  stage.labels {
    values = { status = "status", method = "method" }
  }

  stage.timestamp {
    source = "time_local"
    format = "02/Jan/2006:15:04:05 -0700"
  }

  forward_to = [loki.write.cloud.receiver]
}

// ---------- kernel (OOM) ----------
local.file_match "kernel" {
  path_targets = [{ __path__ = "/var/log/host/kern.log", job = "kernel" }]
}

loki.source.file "kernel" {
  targets    = local.file_match.kernel.targets
  forward_to = [loki.process.kernel.receiver]
}

loki.process "kernel" {
  // Descarta tudo que não é OOM ANTES de enviar — 50 GB/mês é o teto do free tier.
  stage.match {
    selector            = `{job="kernel"} !~ "Memory cgroup out of memory|Out of memory: Killed process"`
    action              = "drop"
    drop_counter_reason = "not_oom"
  }
  // Sem stage.timestamp de propósito: kern.log traz timestamp syslog sem ano, que viraria
  // ano 0000 e o Loki rejeitaria como "too far behind". Usa-se o horário de ingestão.
  forward_to = [loki.write.cloud.receiver]
}

// ---------- API e Web (push do Serilog) ----------
// O sink Serilog.Sinks.Grafana.Loki nao tem parametro de credencial em SerilogExtensions.cs.
// Em vez de dar o token do Grafana Cloud para dois processos .NET, o Alloy recebe o push em
// claro na rede interna e reenvia autenticado. A credencial fica num lugar so.
loki.source.api "app" {
  http {
    listen_address = "0.0.0.0"
    listen_port    = 3100
  }
  forward_to = [loki.write.cloud.receiver]
}
```

- [ ] **Passo 3: dar ao Alloy os binds de log e a env do salt**

Em `docker-compose.yml`, serviço `alloy`, acrescentar aos `volumes` (vêm de
`docker-compose.yml:410,418`):

```yaml
      - /var/log/nginx:/var/log/nginx:ro
      # Bind de DIRETÓRIO, não de arquivo: bind de arquivo fixa o inode e o logrotate
      # quebraria o tail em silêncio (decisão registrada em docker-compose.yml:411-417).
      - /var/log:/var/log/host:ro
```

E ao `environment`:
```yaml
      - GC_LOKI_URL=${GC_LOKI_URL:?defina GC_LOKI_URL no .env}
      - GC_LOKI_USER=${GC_LOKI_USER:?defina GC_LOKI_USER no .env}
      - GC_IP_SALT=${GC_IP_SALT:?defina GC_IP_SALT no .env}
```

- [ ] **Passo 4: redirecionar o Serilog da API e do Web para o Alloy**

Em `docker-compose.yml:11` (serviço `app`) e `:166` (serviço `web`), trocar:
```yaml
      - Loki__Uri=http://loki:3100
```
por:
```yaml
      - Loki__Uri=http://alloy:3100
```

Nenhuma mudança em C# é necessária: `SerilogExtensions.cs:11` já lê `Loki:Uri` da configuração e o
`loki.source.api` fala o mesmo protocolo de push que o `loki` falava.

- [ ] **Passo 5: verificar que os labels do Serilog sobrevivem ao repasse**

`loki.source.api` recebe streams já rotuladas pelo cliente. Provar que ele não sobrescreve o
`job` que o sink manda (`SerilogExtensions.cs:22`), porque o dashboard e as consultas dependem dele:

```bash
ssh root@157.230.148.98 'cd /opt/tesouro-direto && docker compose up -d alloy app web && sleep 20 && curl -sf localhost:5000/health/ready'
```
No Explore do Grafana Cloud (Loki):
```logql
count by (job) (count_over_time({job=~"tesouro-direto-.+"}[5m]))
```
Esperado: **duas** linhas, `job="tesouro-direto-api"` e `job="tesouro-direto-web"`. Se vier um `job`
único ou vazio, acrescentar `labels = {}` explícito ao `loki.source.api` e reverificar.

- [ ] **Passo 6: verificar que o IP saiu hasheado, e não em claro**

```logql
{job="nginx"} |= "" | line_format "{{ __line__ }}" | limit 5
```
Esperado: as linhas começam com hex de 64 chars, **não** com `NNN.NNN.NNN.NNN`. Este é o
controle de LGPD — se aparecer um IP em claro, **parar e corrigir antes de seguir**, porque o dado
já foi transferido.

Controle negativo — provar que o hash distingue clientes (senão o `topk by ip` vira uma linha só):
```logql
count(count by (ip) (count_over_time({job="nginx"} | regexp "^(?P<ip>[a-f0-9]+)" [1h])))
```
Esperado: > 1.

- [ ] **Passo 6b: acrescentar os casos `nginx` e `kernel` ao teste de contrato**

Agora que `config.alloy` declara os dois jobs, o teste pode cobri-los sem nascer vermelho. Em
`AlloyContractTests.cs`, acrescentar a `ConfigAlloy_DeclaraOsJobsQueAsRegrasConsomem`:
```csharp
    [InlineData("nginx")]
    [InlineData("kernel")]
```
```bash
dotnet test tests/TesouroDireto.API.Tests --filter "FullyQualifiedName~AlloyContractTests" -v minimal
```
Esperado: PASS nos 4 casos.

- [ ] **Passo 7: corrigir o painel "Top IPs" que o hash quebra**

O painel usa `| regexp "^(?P<ip>[\d.]+)"`, que casa dígitos e pontos — depois do hash a linha começa
com hex, então o painel devolveria **vazio em silêncio**. Em
`infra/grafana/dashboards/tesouro-direto.json`, painel "Top IPs em 403/429" (~linha 457-464), trocar:

```
| regexp "^(?P<ip>[\\d.]+)"
```
por
```
| regexp "^(?P<ip>[a-f0-9]+)"
```

- [ ] **Passo 8: confirmar que nginx e kernel chegaram com os labels certos**

```logql
count by (job, status, method) (count_over_time({job="nginx"}[10m]))
```
Esperado: linhas com `status` e `method` preenchidos (ex.: `status="200", method="GET"`).

```logql
count_over_time({job="kernel"}[24h])
```
Esperado: **0 ou muito baixo** — o `stage.match` descarta tudo que não é OOM. Um número alto significa
que o drop não pegou e o teto de 50 GB/mês está em risco.

- [ ] **Passo 9: rodar a suíte e commitar**

```bash
dotnet test -v minimal
```
Esperado: 841 + contratos passando.

```bash
git add infra/alloy/config.alloy docker-compose.yml infra/grafana/dashboards/tesouro-direto.json
git commit -m "feat(77.2): alloy assume os 4 fluxos de log, com hash de IP

nginx e kernel por tail; API e Web por loki.source.api, porque o sink Serilog
nao aceita credencial — o token do Cloud fica so no Alloy.

LGPD: stage.replace com Hash (SHA3-256 + salt) no IP antes do envio. O painel
Top IPs foi corrigido de [\\d.] para [a-f0-9], senao devolveria vazio calado.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Fase 77.3 — Migrar dashboards e as 18 regras para a nuvem

**O problema que esta fase resolve.** O Grafana Cloud **não suporta provisioning por arquivo** —
o `infra/grafana/provisioning/` inteiro deixa de ter leitor. O caminho é exportar do Grafana local
pela API, corrigir os UIDs de datasource, e recriar na nuvem pela API de provisioning. Fica tudo
versionado no repo, sem introduzir Terraform.

**Files:**
- Create: `scripts/grafana-cloud/{lib.sh,export-local.sh,apply-cloud.sh}`
- Create: `infra/grafana/cloud/{rules.yaml,contactpoints.yaml,policies.yaml}`

**Interfaces:**
- Consumes: séries e streams já na nuvem (77.1, 77.2).
- Produces: 18 regras ativas na nuvem entregando no Telegram; `infra/grafana/cloud/rules.yaml` como
  fonte da verdade que `AlloyContractTests.TodoJobUsadoNasRegras` lê.

- [ ] **Passo 1: exportar o que já existe, do Grafana local**

Criar `scripts/grafana-cloud/export-local.sh`:

```bash
#!/usr/bin/env bash
# Exporta alerting do Grafana LOCAL no formato de provisioning, para virar fonte da
# verdade em infra/grafana/cloud/. Rodar com um tunel SSH aberto para a VPS:
#   ssh -N -L 3000:127.0.0.1:3000 root@157.230.148.98
set -euo pipefail

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

echo "regras exportadas: $(grep -c '^\s*- uid:' "$OUT/rules.yaml")"
```

```bash
chmod +x scripts/grafana-cloud/export-local.sh
ssh -N -L 3000:127.0.0.1:3000 root@157.230.148.98 &
GRAFANA_PASSWORD="$(ssh root@157.230.148.98 'grep ^GRAFANA_PASSWORD /opt/tesouro-direto/.env | cut -d= -f2-')" \
  ./scripts/grafana-cloud/export-local.sh
```
Esperado: `regras exportadas: 18`. Qualquer outro número significa que o export pegou escopo errado
— **conferir antes de seguir**, porque uma regra perdida aqui é um alerta que some para sempre.

- [ ] **Passo 2: verificar o que veio, contra a lista conhecida**

```bash
grep -oE 'td-[a-z0-9-]+' infra/grafana/cloud/rules.yaml | sort -u | tee /dev/stderr | wc -l
```
Esperado: 18 uids, exatamente estes — `td-frescor-dado`, `td-app-down`, `td-db-readiness-down`,
`td-http-5xx-alto`, `td-http-p95-alto`, `td-import-falha`, `td-simulador-degradado`,
`td-simulador-falha-ratio`, `td-disco-cheio`, `td-rate-limit-anomalo`,
`td-container-memoria-warning`, `td-container-memoria-critical`, `td-container-reclaim-sustentado`,
`td-container-cpu-throttling`, `td-container-restart`, `td-container-oom-kill`,
`td-metricas-container-obsoletas`, `td-oom-kernel-log`.

- [ ] **Passo 3: parametrizar os UIDs de datasource**

Os UIDs locais (`prometheus`, `loki`) não existem na nuvem. Substituir por placeholders que o
`apply-cloud.sh` resolve em tempo de execução.

**Não usar `sed`.** O uid aparece em **dois** lugares por regra: `datasourceUid:` no nível de `data[]`
e `model.datasource.uid:` aninhado (`rules.yaml:326` e `:674` nas 2 regras Loki). Um `sed` sobre
`datasourceUid:` corrige só o primeiro, e o `grep -c` de aceite **passaria com o bug presente** —
falsa confiança exatamente onde dói. Usar `yq`, que anda a árvore:

```bash
yq -i '(.. | select(has("datasourceUid")).datasourceUid) |=
         (sub("^prometheus$", "__DS_PROM__") | sub("^loki$", "__DS_LOKI__")) |
       (.. | select(has("uid") and has("type")).uid) |=
         (sub("^prometheus$", "__DS_PROM__") | sub("^loki$", "__DS_LOKI__"))' \
  infra/grafana/cloud/rules.yaml
```

Aceite — conta as duas formas, não só uma:
```bash
grep -c '__DS_PROM__' infra/grafana/cloud/rules.yaml   # esperado: 32 (16 regras × 2 lugares)
grep -c '__DS_LOKI__' infra/grafana/cloud/rules.yaml   # esperado: 4  (2 regras × 2 lugares)
grep -nE '(datasourceUid|uid): (prometheus|loki)$' infra/grafana/cloud/rules.yaml  # esperado: vazio
```
A terceira linha é a que importa: **qualquer** uid literal remanescente aponta para um datasource
que não existe na nuvem e a regra falha calada.

- [ ] **Passo 4: escrever a lib de resolução de UID**

Criar `scripts/grafana-cloud/lib.sh`:

```bash
#!/usr/bin/env bash
# Helpers para falar com a API do Grafana Cloud. Exige GC_GRAFANA_URL e GC_GRAFANA_TOKEN
# (service account token com role Admin na stack).
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
gc_datasource_uid() {
  local tipo="$1"
  gc_curl GET /api/datasources | jq -r --arg t "$tipo" \
    'map(select(.type == $t)) | .[0].uid // empty'
}

# Cria a pasta se nao existir; devolve o uid nos dois casos (idempotente).
gc_folder_uid() {
  local titulo="$1" existente
  existente=$(gc_curl GET /api/folders | jq -r --arg t "$titulo" \
    'map(select(.title == $t)) | .[0].uid // empty')
  if [ -n "$existente" ]; then echo "$existente"; return; fi
  gc_curl POST /api/folders -d "$(jq -nc --arg t "$titulo" '{title: $t}')" | jq -r '.uid'
}
```

- [ ] **Passo 5: escrever o aplicador**

Criar `scripts/grafana-cloud/apply-cloud.sh`:

```bash
#!/usr/bin/env bash
# Recria na nuvem o alerting versionado em infra/grafana/cloud/.
# Idempotente: reaplica por cima sem duplicar (as regras tem uid fixo td-*).
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/grafana-cloud/lib.sh

DS_PROM=$(gc_datasource_uid prometheus)
DS_LOKI=$(gc_datasource_uid loki)
[ -n "$DS_PROM" ] || { echo "datasource prometheus nao encontrado na nuvem" >&2; exit 1; }
[ -n "$DS_LOKI" ] || { echo "datasource loki nao encontrado na nuvem" >&2; exit 1; }
echo "datasources: prom=$DS_PROM loki=$DS_LOKI"

FOLDER_UID=$(gc_folder_uid TesouroDireto)
echo "folder TesouroDireto: $FOLDER_UID"

TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

sed -e "s/__DS_PROM__/${DS_PROM}/g" \
    -e "s/__DS_LOKI__/${DS_LOKI}/g" \
    infra/grafana/cloud/rules.yaml > "$TMP/rules.yaml"

# O contact point traz o token por env, igual ao provisioning local.
sed -e "s|\${TELEGRAM_BOT_TOKEN}|${TELEGRAM_BOT_TOKEN:?defina TELEGRAM_BOT_TOKEN}|g" \
    infra/grafana/cloud/contactpoints.yaml > "$TMP/contactpoints.yaml"

post_yaml() {
  local path="$1" arquivo="$2"
  curl -sf -X POST \
    -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
    -H 'Content-Type: application/yaml' \
    -H 'X-Disable-Provenance: true' \
    --data-binary "@${arquivo}" \
    "${GC_GRAFANA_URL}${path}"
}

post_yaml /api/v1/provisioning/contact-points "$TMP/contactpoints.yaml"
curl -sf -X PUT \
  -H "Authorization: Bearer ${GC_GRAFANA_TOKEN}" \
  -H 'Content-Type: application/yaml' \
  -H 'X-Disable-Provenance: true' \
  --data-binary "@infra/grafana/cloud/policies.yaml" \
  "${GC_GRAFANA_URL}/api/v1/provisioning/policies"
post_yaml /api/v1/provisioning/alert-rules "$TMP/rules.yaml"

# Dashboards no MESMO processo: DS_PROM/DS_LOKI/FOLDER_UID sao variaveis locais deste
# script. Um bloco bash separado nao as enxerga e `jq --arg p ""` gravaria uid vazio em
# todos os paineis com HTTP 200 — dashboard salvo e quebrado, sem erro nenhum.
for d in tesouro-direto host load-test; do
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

echo "aplicado. conferindo:"
gc_curl GET /api/v1/provisioning/alert-rules | jq 'length'
```

```bash
chmod +x scripts/grafana-cloud/{lib.sh,apply-cloud.sh}
```

- [ ] **Passo 6: aplicar e conferir a contagem**

```bash
export GC_GRAFANA_URL="https://<sua-stack>.grafana.net"
export GC_GRAFANA_TOKEN="$(cat scratchpad/gc_grafana_token)"
export TELEGRAM_BOT_TOKEN="$(ssh root@157.230.148.98 'grep ^TELEGRAM_BOT_TOKEN /opt/tesouro-direto/.env | cut -d= -f2-')"
./scripts/grafana-cloud/apply-cloud.sh
```
Esperado: última linha imprime `18`.

Se o endpoint de POST em lote não aceitar o YAML de export inteiro, iterar regra a regra —
`yq '.groups[].rules[]'` e um POST por regra. O critério de aceite é o mesmo: 18 no final.

- [ ] **Passo 7: conferir os 3 dashboards que o script acabou de importar**

A saída do Passo 6 deve ter trazido, antes do `18`: `success tesouro-direto-api`,
`success host-vps`, `success load-test-k6`.

Provar que os UIDs foram resolvidos de verdade — o modo de falha aqui é silencioso (HTTP 200 com
`uid` vazio em todo painel):
```bash
curl -sf -H "Authorization: Bearer $GC_GRAFANA_TOKEN" \
  "$GC_GRAFANA_URL/api/dashboards/uid/tesouro-direto-api" \
  | jq '[.dashboard | .. | objects | select(has("uid") and has("type")) | .uid] | unique'
```
Esperado: os UIDs reais da nuvem (algo como `["grafanacloud-<org>-logs","grafanacloud-<org>-prom"]`).
Se aparecer `""` ou `"prometheus"`/`"loki"`, a resolução falhou.

Abrir cada um na UI e conferir que nenhum painel mostra "No data" por datasource não resolvido.
O `load-test.json` fica sem dado até rodar carga — isso é esperado, não é falha.

- [ ] **Passo 8: provar que o Telegram entrega, da nuvem**

Na UI do Grafana Cloud: **Alerts & IRM → Contact points → telegram-tesouro → Test**.
Esperado: mensagem chega no Telegram. Se não chegar, o `bottoken` não foi interpolado — conferir
que o `chatid` continua **string literal** `"144442958"` (o mesmo bug de crash loop da O9 vale aqui:
valor interpolado não funciona em forma nenhuma).

- [ ] **Passo 9: acrescentar o teste de contrato que só agora tem o que verificar**

`infra/grafana/cloud/rules.yaml` passa a existir nesta fase — só agora o teste pode nascer verde.
Em `AlloyContractTests.cs`, acrescentar `using System.Text.RegularExpressions;` no topo e:

```csharp
    [Fact]
    public void TodoJobUsadoNasRegras_ExisteNoConfigAlloy()
    {
        var root = RepoRoot();
        var rules = File.ReadAllText(Path.Combine(root, "infra/grafana/cloud/rules.yaml"));
        var config = File.ReadAllText(Path.Combine(root, "infra/alloy/config.alloy"));

        var jobs = Regex.Matches(rules, @"job\s*=\s*""([^""]+)""")
                        .Select(m => m.Groups[1].Value)
                        .Distinct();

        Assert.NotEmpty(jobs);   // senao o regex nao casou nada e o teste passa vazio
        foreach (var job in jobs)
            Assert.True(config.Contains($"\"{job}\""),
                $"job \"{job}\" usado em rules.yaml não existe em config.alloy");
    }
```

```bash
dotnet test tests/TesouroDireto.API.Tests --filter "FullyQualifiedName~AlloyContractTests" -v minimal
```
Esperado: PASS. O `Assert.NotEmpty` existe porque um regex que não casa nada faria o `foreach` não
executar e o teste passar sem verificar coisa nenhuma.

- [ ] **Passo 10: rodar a suíte e commitar**

```bash
dotnet test -v minimal && git add scripts/grafana-cloud infra/grafana/cloud && git commit -m "feat(77.3): alerting e dashboards migrados para o Grafana Cloud

O Cloud NAO suporta provisioning por arquivo. As 18 regras sao exportadas do
Grafana local pela API, versionadas em infra/grafana/cloud/ com os uids de
datasource parametrizados, e reaplicadas por script. Sem Terraform.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Fase 77.4 — Cortar a stack local

**Só entrar aqui com as 18 regras verdes na nuvem por pelo menos 1h.** Esta é a fase irreversível.

**Files:**
- Modify: `docker-compose.yml` (remove 5 serviços; `prometheus` volta sob profile)
- Modify: `docker-compose.load.yml`, `run-load.sh`, `infra/nginx/tesouro-direto.conf`,
  `.github/workflows/deploy.yml`
- Delete: `infra/loki/`, `infra/promtail/`, `infra/grafana/provisioning/`

- [ ] **Passo 1: manter o `prometheus` vivo, mas só para teste de carga**

O k6 faz remote-write para o Prometheus local (`docker-compose.load.yml:17,28`) e a memória do
projeto proíbe mandar `k6_*` para a nuvem. Em vez de deletar o serviço, colocá-lo atrás de um
profile: custo **zero** em repouso, e `docker-compose.load.yml` continua válido.

Em `docker-compose.yml`, no serviço `prometheus`, acrescentar:
```yaml
    profiles: ["load"]
```
Manter `image`, `command`, volumes e limites como estão. Sem isso, o overlay `load` define um
serviço `prometheus` sem `image` e `docker compose config -q` falha no gate do CI
(`deploy.yml:72`) com "service must define image or build".

- [ ] **Passo 2: remover os 4 serviços que não voltam**

Em `docker-compose.yml`, deletar os blocos `grafana` (205-245), `loki` (247-283),
`node-exporter` (322-394) e `promtail` (396-436). Em `volumes:` (438-443), deletar
`grafana-data`, `loki-data` e `promtail-positions`. **Manter `prometheus-data`** (o serviço
sob profile ainda usa).

- [ ] **Passo 3: validar as 3 combinações do gate do CI**

```bash
GC_PROM_URL=http://d GC_PROM_USER=1 GC_TOKEN=d GC_LOKI_URL=http://d GC_LOKI_USER=1 GC_IP_SALT=d \
DB_PASSWORD=d API_KEY=d K6_PROMETHEUS_RW_SERVER_URL=http://d:9090/prometheus/api/v1/write \
  sh -c 'docker compose config -q && echo base OK &&
         docker compose -f docker-compose.yml -f docker-compose.profiling.yml config -q && echo profiling OK &&
         docker compose -f docker-compose.yml -f docker-compose.load.yml --profile load config -q && echo load OK'
```
Esperado: as 3 linhas `OK`. Note que `GRAFANA_PASSWORD` e `TELEGRAM_BOT_TOKEN` **não** são mais
necessárias — é a prova de que os `${VAR:?}` saíram junto com o serviço `grafana`.

- [ ] **Passo 4: consertar o deploy.yml — a quebra dura**

`.github/workflows/deploy.yml:214` tem nomes de serviço hard-coded:
```yaml
docker compose up -d --force-recreate --no-deps prometheus grafana promtail
```
Sem esses serviços isso vira `no such service: prometheus`, exit != 0, e o job **falha depois** do
nginx já ter sido recarregado na linha 200 — deploy pela metade. Trocar por:
```yaml
          # Bind-mount de config nao e recriado por `up -d` quando o arquivo muda
          # (feedback compose_bindmount_no_recreate). O alloy tem a mesma armadilha
          # que prometheus/grafana/promtail tinham: config.alloy vem de bind-mount.
          docker compose up -d --force-recreate --no-deps alloy
```

Em `deploy.yml:63-65`, remover as dummies `GRAFANA_PASSWORD` e `TELEGRAM_BOT_TOKEN` e acrescentar:
```yaml
          GC_PROM_URL: http://dummy
          GC_PROM_USER: "1"
          GC_TOKEN: dummy
          GC_LOKI_URL: http://dummy
          GC_LOKI_USER: "1"
          GC_IP_SALT: dummy
```

Em `deploy.yml:167-176`, no `printf` que reescreve o `.env` da VPS por completo: remover
`GRAFANA_PASSWORD` e `GRAFANA_ROOT_URL`, **manter `TELEGRAM_BOT_TOKEN`** (agora usado pelo
`apply-cloud.sh`, não mais por container) e acrescentar as 6 novas `GC_*`. Cadastrar os secrets
correspondentes no GitHub antes de mergear — o `.env` é reescrito inteiro a cada deploy, e uma
variável faltando derruba o boot num `${VAR:?}`.

- [ ] **Passo 5: remover as rotas nginx que viraram 502**

Em `infra/nginx/tesouro-direto.conf`, deletar os 4 blocos — eles estão **duplicados** entre o server
de 443 e o de 3080, e os dois precisam mudar juntos:
- `location /grafana/` → `127.0.0.1:3000` — linhas 75-83 **e** 166-174
- `location /prometheus/` → `127.0.0.1:9090` — linhas 103-108 **e** 194-199

**Manter** `location /api/metrics` (94-101 e 185-192): ele faz proxy para o `/metrics` do app, que
continua existindo (`Program.cs:61`), e é acesso humano de diagnóstico.

```bash
ssh root@157.230.148.98 'nginx -t'
```
Esperado: `syntax is ok` / `test is successful`. Conferir com `nginx -T | grep -c grafana` → `0`.

- [ ] **Passo 6: consertar o run-load.sh**

Em `run-load.sh:74-75`, acrescentar o profile — **reaproveitando as variáveis do script**
(`COMPOSE_FILE`/`LOAD_COMPOSE_FILE`, definidas em `run-load.sh:39-40` com `$SCRIPT_DIR`), senão
o script deixa de funcionar quando chamado de fora da raiz do repo:
```bash
docker compose -f "$COMPOSE_FILE" -f "$LOAD_COMPOSE_FILE" --profile load up -d prometheus
```
Em `run-load.sh:107`, a URL do dashboard passa a apontar para a nuvem:
```bash
echo "Dashboard: ${GC_GRAFANA_URL:-https://<sua-stack>.grafana.net}/d/load-test-k6"
```
O `load-test.json` agora vive na nuvem mas lê do Prometheus **local**, que a nuvem não alcança.
Registrar isso no `infra/alloy/README.md` como limitação conhecida: durante o teste de carga, ver o
dashboard k6 exige túnel SSH para o Prometheus efêmero. A alternativa (mandar `k6_*` para a nuvem)
está proibida por decisão registrada.

- [ ] **Passo 7: deletar os arquivos órfãos**

```bash
git rm -r infra/loki infra/promtail infra/grafana/provisioning
```
**Não deletar** `infra/grafana/dashboards/` (os 3 JSON continuam sendo fonte da verdade, importados
pelo script da 77.3) nem `prometheus.yml` (usado pelo serviço sob profile `load`).

Limpar também os dois comandos ruby órfãos em `.claude/settings.local.json:61,66`, que validavam o
`chatid` do `contactpoints.yaml` que acabou de sair.

- [ ] **Passo 7b: podar o scrape job morto do `prometheus.yml`**

O `prometheus.yml` sobrevive para o profile `load`, mas o job `node` (`prometheus.yml:18-20`) aponta
para `node-exporter:9100`, que deixou de existir em qualquer combinação do compose. Deixá-lo faz o
Prometheus efêmero errar DNS a cada scrape sem necessidade. Remover o bloco `job_name: 'node'` e o
self-scrape `job_name: 'prometheus'` (:22-32), deixando só `tesouro-direto-api` — é o único de que
o teste de carga precisa.

- [ ] **Passo 7c: atualizar o `.env.example`**

Ele é o contrato para quem clona o repo. Hoje lista `GRAFANA_PASSWORD` (:12) e `GRAFANA_ROOT_URL`
(:26) como necessárias e não menciona nenhuma `GC_*` — um clone novo travaria no
`${GC_PROM_URL:?}` sem pista de onde tirar o valor. Remover as duas, manter `TELEGRAM_BOT_TOKEN`
(agora consumido pelo `apply-cloud.sh`) e acrescentar na seção "Obrigatórias":

```bash
# --- Grafana Cloud (obtidas na pagina "Details" da stack; ver infra/alloy/README.md) ---
GC_PROM_URL=
GC_PROM_USER=
GC_LOKI_URL=
GC_LOKI_USER=
# Access Policy Token com escopos metrics:write e logs:write.
GC_TOKEN=
# Salt do hash de IP no pipeline do nginx (LGPD). Gere com: openssl rand -hex 32
GC_IP_SALT=
```

- [ ] **Passo 7d: remover os volumes órfãos da VPS**

Apagar as chaves do bloco `volumes:` não remove os volumes do host — eles ficariam ocupando disco
para sempre. Conferir antes de apagar, porque `docker volume rm` é irreversível:

```bash
ssh root@157.230.148.98 'docker volume ls --format "{{.Name}}" | grep -E "grafana-data|loki-data|promtail-positions"'
```
Esperado: 3 nomes (com o prefixo do projeto). Conferir o tamanho antes:
```bash
ssh root@157.230.148.98 'docker system df -v | grep -E "grafana-data|loki-data|promtail-positions"'
```
Só então:
```bash
ssh root@157.230.148.98 'docker volume rm tesouro-direto_grafana-data tesouro-direto_loki-data tesouro-direto_promtail-positions'
```
**Manter `prometheus-data`** — o serviço sob profile `load` ainda usa.

- [ ] **Passo 8: rodar a suíte inteira e o E2E**

```bash
dotnet test -v minimal
./run-e2e.sh
```
Esperado: 841+ passando; E2E verde. Atenção a `tests/TesouroDireto.E2E.Tests/tests/health.spec.ts:10-18`,
que assere `GET /metrics` → 200 com `process_cpu_seconds_total` no corpo — deve continuar passando,
porque `MapMetrics()` não foi tocado. Se falhar, algo removeu o endpoint por engano.

- [ ] **Passo 9: medir a economia real**

```promql
sum(td_container_memory_unreclaimable_bytes) / 1024 / 1024
```
Comparar com o baseline de 1060 MB (53,9% da VPS) da tarefa 76. Registrar o número — é o resultado
que justifica (ou não) a tarefa inteira.

- [ ] **Passo 10: commit**

```bash
git add -A && git commit -m "feat(77.4): remove a stack de observabilidade local

grafana/loki/promtail/node-exporter saem. prometheus fica sob profile 'load'
porque o k6 faz remote-write local e mandar k6_* para a nuvem estouraria o
teto de series justamente durante o teste.

deploy.yml:214 tinha os nomes hard-coded e falharia DEPOIS do reload do nginx,
deixando deploy pela metade. nginx perde /grafana/ e /prometheus/ nos dois
server blocks (443 e 3080).

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Fase 77.5 — Gate de 24h, dead-man's switch e docs

- [ ] **Passo 1: provar o ganho que motivou a migração — alerta sobrevivendo à queda da VPS**

Este é o **único** teste que o desenho antigo não tinha como passar. Com o alerting dentro da VPS,
derrubar a VPS calava o alerta.

```bash
ssh root@157.230.148.98 'cd /opt/tesouro-direto && docker compose stop app'
```
Esperado: em ~2 min (`td-app-down`, `for: 2m`, `noDataState: Alerting`) chega mensagem no Telegram.
```bash
ssh root@157.230.148.98 'cd /opt/tesouro-direto && docker compose start app'
```
Esperado: resolve.

- [ ] **Passo 2: provar o dead-man's switch do textfile collector**

```bash
ssh root@157.230.148.98 'systemctl stop td-container-metrics.timer'
```
Esperado: em ~5 min `td-metricas-container-obsoletas` dispara (`> 300`, `noDataState: Alerting`).
```bash
ssh root@157.230.148.98 'systemctl start td-container-metrics.timer'
```
Esperado: resolve.

- [ ] **Passo 3: gate de 24h**

Reaproveitar o padrão de `/root/fp-gate76.sh` (cron de 5 min, CSV em `/var/tmp/`), agora com o
`alloy` na lista. Critério: **zero `oom_kill`, zero restart** nos containers restantes por 24h, e
`td_container_memory_unreclaimable_bytes{container="tesouro-direto-alloy"}` abaixo do teto fixado.
**Remover o cron ao fechar** — é a pendência que ficou aberta na 74.3 e na 76.

- [ ] **Passo 4: conferir o consumo contra o free tier depois de 24h reais**

- Séries: `count({__name__=~".+"}) * 2` < 8000
- Log: no Cloud Portal → Billing/Usage, projeção mensal < 40 GB (margem de 20% sobre 50 GB)
- Primeira regra a criar na nuvem: alerta sobre o **próprio discard rate** do Grafana Cloud —
  se a ingestão passar a ser rejeitada, você precisa saber por alerta, não por dashboard vazio.

- [ ] **Passo 5: atualizar as docs**

Por densidade de menções à stack antiga: `docs/PLANO.md` (182), `docs/load/footprint.md` (87),
`docs/load/README.md` (31), `docs/MAPA.md` (29), `README.md` (29),
`docs/analises/observabilidade.md` (25), `infra/host/README.md` (12), `docs/load/profiling.md` (5),
`WORKFLOW.md` (3).

Prioridade: `README.md` e `docs/MAPA.md` (porta de entrada), `infra/host/README.md` (instrução de
instalação manual em VPS nova, que agora aponta para o Alloy e não para o node-exporter).
`docs/PLANO.md` é histórico — acrescentar a entrada da 77, não reescrever o passado.

Notar também que `setup.sh:52-99` é o gerador de scaffold e ainda emite a stack antiga; se rodado de
novo, **regenera grafana/loki/prometheus por cima**. Corrigir ou marcar como obsoleto.

- [ ] **Passo 6: fechar a tarefa nos 3 lugares**

`docs/PLANO.md`, a tabela de status e `docs/MAPA.md` — o padrão do projeto.

- [ ] **Passo 7: revisão adversarial**

Despachar o subagent `revisor` para tentar refutar a entrega. **Esta etapa foi cortada na 76 por
pressa e os dois erros de métrica só apareceram depois do merge.** Não cortar aqui.

- [ ] **Passo 8: gravar na memória**

A decisão, o motivo e as alternativas rejeitadas — incluindo o número medido do Alloy na 77.0, que é
o dado que faltava na decisão original de 11/08.

---

## Riscos, com quem trava cada um

| risco | probabilidade | mitigação |
|---|---|---|
| **Alloy custa mais memória que a stack que ele substitui** | média — relatos públicos vão de 400 a 742 MB | Fase 77.0 é portão explícito; mede antes de remover qualquer coisa |
| `loki.source.api` sobrescreve o `job` do Serilog | média | 77.2 passo 5 verifica com `count by (job)` antes de seguir |
| `prometheus.exporter.unix` rotula com `job` próprio e as 7 regras de container ficam mudas | **alta** | `discovery.relabel` + verificação explícita em 77.1 passo 7 |
| IP em claro chega à nuvem antes do hash funcionar | baixa, impacto alto (LGPD) | 77.2 passo 6 é bloqueante; o dado já transferido não volta |
| Export de regras traz menos de 18 | média | 77.3 passos 1-2 conferem a contagem e a lista de uids |
| **UID aninhado (`model.datasource.uid`) não substituído nas 2 regras Loki** — e o aceite passa mesmo assim | **alta** se usar `sed` | 77.3 passo 3 usa `yq` e o aceite grepa por uid literal remanescente, não por contagem de placeholder |
| Config do Alloy só é validada na VPS | alta | 77.0 passo 3b roda `alloy fmt` local, repetido a cada fase que edita o arquivo |
| Deploy quebra na linha 214 e para depois do reload do nginx | **certa** se não tratada | 77.4 passo 4 |
| Retenção cai de 7d (métrica) e 7d (log) para 14d na nuvem | — | é **ganho** nos dois casos; a perda de 30d→14d que o dono aceitou em 11/08 já tinha virado 7d na tarefa 74.2 |

## O que esta tarefa NÃO faz

- Não mexe em `limit_req` do nginx nem em `worker_connections` (follow-up separado da 74.5).
- Não conserta o scanner do Sonar (item 1 do backlog, independente).
- Não migra o `chart.js` do CDN (follow-up incidental da 72).
- Não remove o `prometheus` do repo — ele sobrevive sob profile `load` para o k6.
