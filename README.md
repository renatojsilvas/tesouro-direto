# Tesouro Direto API

API e front para consultar **títulos do Tesouro Direto**, seus **preços/taxas** (histórico e mais recente) e **simular investimentos** — valor bruto e líquido com **IOF** (tabela regressiva de 29 dias) e **IR** (faixa regressiva) já descontados, cupons semestrais quando o título paga juros, e **comparação de cenários** de projeção lado a lado. Os dados são ingeridos automaticamente de fontes públicas (Tesouro Transparente, ANBIMA e BCB Focus) e servidos por uma API REST (nível 3 de Richardson) consumida por uma interface web em Blazor.

> Solução .NET 8 em **Clean Architecture / Ports & Adapters**, mono-domínio com contextos (Títulos, Preços/Taxas, Tributos, Simulador, Feriados, Dias Úteis). Documentação de detalhe fino vive em [`docs/`](#estrutura-de-documentação) — este README **resume e aponta**, não duplica.

---

## Sumário

- [Arquitetura](#arquitetura)
- [API](#api)
- [Setup local](#setup-local)
- [Deploy e operação](#deploy-e-operação)
- [Estrutura de documentação](#estrutura-de-documentação)

---

## Arquitetura

Dois pontos de entrada — **API Minimal** (`TesouroDireto.API`) e **Blazor Server** (`TesouroDireto.Web`), que consome a API por HTTP. Ingestão por **jobs Quartz** de fontes públicas. Stack de **observabilidade** própria (logs + métricas + alertas).

```mermaid
flowchart TB
    browser([Navegador])

    subgraph host["Host (VPS) — fora do docker compose"]
        nginx["nginx :3080<br/>proxy reverso + rate limit + gzip"]
    end

    subgraph compose["docker compose (rede tesouro-net, portas em 127.0.0.1)"]
        web["web — Blazor Server :5275"]
        app["app — API Minimal :5000"]
        db[("db — PostgreSQL 16 :5432")]

        subgraph obs["Observabilidade"]
            prometheus["prometheus :9090"]
            loki["loki :3100"]
            promtail["promtail"]
            grafana["grafana :3000"]
            nodeexporter["node-exporter :9100"]
        end
    end

    subgraph ext["Fontes externas (jobs Quartz + on-demand)"]
        tesouro["Tesouro Transparente<br/>(CSV, diário 06:00)"]
        anbima["ANBIMA<br/>(XLS feriados, anual 1º/dez)"]
        bcb["BCB Focus<br/>(OData, cache 6h + lkg 7d)"]
    end

    telegram([Telegram])

    browser -->|HTTP| nginx
    nginx -->|"/"| web
    nginx -->|"/api/"| app
    nginx -.->|"/grafana/ /prometheus/ /api/swagger — só localhost (túnel SSH)"| grafana
    web -->|HTTP + X-Api-Key| app
    app --> db
    app -->|EF Core write / Dapper read| db
    app --> tesouro
    app --> anbima
    app --> bcb

    prometheus -->|scrape /metrics| app
    prometheus -->|scrape| nodeexporter
    promtail -->|logs nginx| loki
    app -->|Serilog| loki
    grafana --> prometheus
    grafana --> loki
    grafana -->|alertas| telegram
```

**Serviços do `docker-compose.yml`:** `app`, `db`, `web`, `grafana`, `loki`, `prometheus`, `node-exporter`, `promtail`. O **nginx roda no host** (não é serviço do compose) — sua config vem de [`infra/nginx/tesouro-direto.conf`](infra/nginx/tesouro-direto.conf) e é copiada para o nginx do sistema no deploy. Todas as portas do compose ficam ligadas a `127.0.0.1` (só a borda nginx é pública).

### Camadas do código (`src/`)

| Projeto | Papel |
|---------|-------|
| `TesouroDireto.Domain` | Entidades, Value Objects e regras; **Result Pattern** (sem exceções para fluxo de negócio). Zero dependências externas. |
| `TesouroDireto.Application` | Casos de uso em **CQRS via MediatR** (commands/queries) e *ports* (interfaces). `LoggingBehavior` no pipeline. |
| `TesouroDireto.Infrastructure` | *Adapters*: persistência (**EF Core** para escrita, **Dapper** para leitura → DTOs), clientes HTTP das integrações, cache, observabilidade. |
| `TesouroDireto.API` | Minimal API — endpoints finos que traduzem `Result` → HTTP; middleware de ApiKey/correlação; Swagger. |
| `TesouroDireto.Web` | Blazor Server; consome a API pelo typed client `TesouroApiClient`. |

Regra arquitetural travada por teste (`TesouroDireto.Architecture.Tests`): Application e Domain não referenciam Infrastructure.

### Decisões-chave (racional curto — detalhe em [`docs/MAPA.md`](docs/MAPA.md) e nas notas de *Feito* de [`docs/PLANO.md`](docs/PLANO.md))

- **`codigo` como identidade pública.** O identificador de título exposto na API é um *slug* determinístico de tipo + data de vencimento (ex.: `tesouro-selic-2029-03-01`), **derivado na leitura** (não persistido, sem coluna nem migration). Racional: o `uuid` interno é chave sintética que muda em reconstrução do banco (restore, nova fonte); a identidade natural sobrevive e desambigua títulos de mesmo tipo/ano com dias diferentes.
- **O `uuid` de Título não trafega no contrato público.** Rotas, request e DTOs de **títulos/preços** usam `codigo` (ou `nome`); o `uuid` de Título permanece só como chave de domínio interna (tarefa 38). O recurso de **configuração de tributos** é um caso à parte e segue identificado por `id` (`GET/PUT /configuracoes/tributos/{id}`, `Location` do `201`).
- **REST nível 3.** Leituras têm `ETag`/`304` condicional e `Cache-Control` (só em respostas 2xx), paginação por `Link` (RFC 8288) + `X-Total-Count`, `_links` HAL-like nos títulos, `405` + `Allow`, `HEAD`/`OPTIONS`. O front consome tudo isso (GET condicional, paginação real por `Link`).
- **Seed idempotente no boot.** Tributos (IOF/IR) e feriados são semeados na inicialização via CQRS — o banco sobe utilizável sem passo manual.
- **Resiliência + fallback nas integrações.** Retry/timeout (Polly) nos três clientes HTTP e *circuit breaker* no BCB; a projeção BCB tem cache *fresh* (6h) + *last-known-good* (7d), com fallback **apenas** em erro HTTP e **nunca silencioso** (log + campo `Origem` na resposta).
- **Observabilidade em 5 camadas** — negócio → app → dependências → borda (nginx) → host (VPS). Ver [`docs/analises/observabilidade.md`](docs/analises/observabilidade.md).

### Modelo de dados (resumo)

PostgreSQL, tabelas `snake_case`: `titulos`, `precos_taxas` (FK `titulo_id`), `tributos` + `tributo_faixas` (owned), `feriados`. Escrita por EF Core (migrations automáticas em Dev/Prod); leitura por Dapper devolvendo DTOs, com decorators de cache. Detalhe (índices, VOs, invariantes) em [`docs/MAPA.md`](docs/MAPA.md).

---

## API

Base local: `http://localhost:5000`. Em produção, via nginx: `http://SEU_HOST:3080/api`.

**Autenticação:** toda rota de negócio exige o header `X-Api-Key`. São **isentas**: `/health*`, `/metrics` e `/swagger`. Falha de autenticação retorna `401` em `application/problem+json`.

**Swagger (OpenAPI):** UI sempre montada em `/swagger`, sem exigir chave. Em produção o acesso é **só por túnel SSH** (o nginx bloqueia origem externa em `/api/swagger`) — ver [Deploy e operação](#deploy-e-operação).

### Rotas

As rotas de **leitura de negócio** (as `GET` da tabela abaixo, exceto `/health*` e `/metrics`, que são sondas de infraestrutura) aceitam também `HEAD`/`OPTIONS`, respondem `405` + `Allow` a métodos não suportados e enviam `ETag`, respondendo `304` a `If-None-Match` casado.

| Método | Rota | Auth | Descrição |
|--------|------|:----:|-----------|
| `GET` | `/health`, `/health/ready` | — | Readiness (checa o banco); `503` se o DB estiver fora. |
| `GET` | `/health/live` | — | Liveness (não toca o banco). |
| `GET` | `/metrics` | — | Métricas Prometheus. |
| `GET` | `/titulos?indexador&vencido` | `X-Api-Key` | Lista títulos (filtros opcionais); itens trazem `_links`. |
| `GET` | `/titulos/{codigo}` | `X-Api-Key` | Título por `codigo` (recurso único, `_links`). |
| `GET` | `/titulos/{codigo}/preco-atual` | `X-Api-Key` | Preço/taxa mais recente por `codigo`. |
| `GET` | `/titulos/{codigo}/precos?dataInicio&dataFim&page&pageSize` | `X-Api-Key` | Histórico por `codigo`; paginação opcional (`Link` + `X-Total-Count`). |
| `GET` | `/titulos/preco-atual?nome` | `X-Api-Key` | Preço/taxa mais recente por `nome`. |
| `GET` | `/titulos/precos?nome&dataInicio&dataFim` | `X-Api-Key` | Histórico por `nome` (intervalo opcional). |
| `GET` | `/configuracoes/tributos` | `X-Api-Key` | Lista tributos (IOF/IR) e faixas. |
| `GET` | `/configuracoes/tributos/{id}` | `X-Api-Key` | Tributo por id (alvo do `Location` do 201). |
| `POST` | `/configuracoes/tributos` | `X-Api-Key` | Cria tributo; `201` + `Location`. |
| `PUT` | `/configuracoes/tributos/{id}` | `X-Api-Key` | Atualiza tributo; `204`. |
| `POST` | `/simulador` | `X-Api-Key` | Simula um investimento; `Cache-Control: no-store`. |
| `POST` | `/simulador/cenarios` | `X-Api-Key` | Simula múltiplos cenários de projeção. |
| `POST` | `/importacao` | `X-Api-Key` | Dispara a importação do CSV do Tesouro (também roda por Quartz). |
| `POST` | `/importacao/feriados` | `X-Api-Key` | Dispara a importação de feriados da ANBIMA (também por Quartz). |

### Exemplos (curl)

Substitua `$API_KEY` pela sua chave e `{codigo}` por um `codigo` real (obtido em `/titulos`). Num ambiente recém-subido, dispare primeiro a importação para popular títulos e preços:

```bash
# 0. Popular títulos/preços a partir do CSV oficial (Tesouro Transparente)
curl -X POST http://localhost:5000/importacao -H "X-Api-Key: $API_KEY"

# 1. Listar títulos (cada item traz _links de navegação)
curl -s http://localhost:5000/titulos -H "X-Api-Key: $API_KEY"

# 2. Preço/taxa mais recente de um título
curl -s "http://localhost:5000/titulos/{codigo}/preco-atual" -H "X-Api-Key: $API_KEY"

# 3. Histórico paginado — repare em ETag, Link e X-Total-Count nos headers
curl -si "http://localhost:5000/titulos/{codigo}/precos?page=1&pageSize=25" -H "X-Api-Key: $API_KEY"

# 4. Repetir enviando o ETag recebido → 304 Not Modified (sem corpo)
curl -si "http://localhost:5000/titulos/{codigo}/precos?page=1&pageSize=25" \
  -H "X-Api-Key: $API_KEY" -H 'If-None-Match: "COLE_O_ETAG_AQUI"'
```

---

## Setup local

### Pré-requisitos

- **.NET 8 SDK** (target `net8.0`).
- **Docker** + plugin **Docker Compose** (usado tanto para subir a stack quanto pelos testes de integração via Testcontainers).
- **Node 20** — apenas para os testes E2E (Playwright).

### 1. Configurar o `.env`

Copie o template e preencha:

```bash
cp .env.example .env
```

O `docker-compose.yml` **falha o boot** se as variáveis obrigatórias estiverem ausentes ou vazias (`${VAR:?}`). Defina, **por nome** (valores são seus — para uso local qualquer valor não-vazio serve; os reais só importam em produção):

| Variável | Obrigatória | Observação |
|----------|:-----------:|------------|
| `API_KEY` | sim | Chave compartilhada entre API e Web (`X-Api-Key`). |
| `GRAFANA_PASSWORD` | sim | Senha do admin do Grafana. |
| `TELEGRAM_BOT_TOKEN` | sim | Token do bot para entrega de alertas (placeholder serve localmente). |
| `DB_PASSWORD` | não | Senha do Postgres (default `app123`). |
| `GRAFANA_ROOT_URL` | não | URL raiz do Grafana (tem default). |

> Nunca faça commit do `.env` — ele está no `.gitignore`. Este README **não** contém valores, só nomes.

### 2. Subir a stack

```bash
docker compose up -d --build
```

Na inicialização a API roda as **migrations** e o **seed idempotente** (tributos IOF/IR + feriados). Endpoints:

- API — `http://localhost:5000` (Swagger em `http://localhost:5000/swagger`)
- Web (Blazor) — `http://localhost:5275`
- Grafana — `http://localhost:3000` · Prometheus — `http://localhost:9090`

Verifique a saúde:

```bash
curl -sf http://localhost:5000/health/ready && echo OK
```

Os **títulos/preços** só aparecem após uma importação (`POST /importacao`, exemplo acima) ou o job diário das 06:00.

### 3. Rodar os testes

```bash
# Suíte completa (xUnit + bUnit do Web) — precisa do Docker rodando (Testcontainers)
dotnet test
```

A suíte cobre Domain, Application, Infrastructure, API (integração HTTP com Postgres via Testcontainers), Web (bUnit) e Architecture. Os testes de cobertura de UI Blazor entram pelo projeto `TesouroDireto.Web.Tests`.

**E2E (Playwright)** — sobe a stack de E2E, semeia um banco efêmero e roda os specs:

```bash
# uma vez, para instalar o runner
cd tests/TesouroDireto.E2E.Tests && npm ci && npx playwright install --with-deps chromium && cd -

# a cada execução (a partir da raiz)
./run-e2e.sh
```

---

## Deploy e operação

### Pipeline (`.github/workflows/deploy.yml`)

`push` na `main` dispara três jobs em sequência: **test → e2e → deploy**.

1. **test** — `dotnet restore/build/test` com cobertura, seguido do **gate de cobertura** (`scripts/coverage-gate.py`, piso de linha em **80%**) e, se houver token, scan do SonarQube. `pull_request` roda só o `test`.
2. **e2e** — sobe `docker-compose.e2e.yml`, aguarda saúde, semeia `seed.sql` e roda os specs Playwright.
3. **deploy** — via SSH no VPS: escreve o `.env`, `git fetch` + `reset --hard origin/main`, copia a config do nginx e recarrega, `docker compose build --no-cache && up -d` (com `--force-recreate` de Prometheus/Grafana, cujas configs são bind-mounts), aguarda `/health/ready` e limpa imagens órfãs.

**Secrets do GitHub necessários** (nomes apenas — valores nos *Settings → Secrets* do repositório):

`API_KEY`, `DB_PASSWORD`, `GRAFANA_PASSWORD`, `GRAFANA_ROOT_URL`, `TELEGRAM_BOT_TOKEN`, `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `SONAR_TOKEN`, `SONAR_HOST_URL`.

### Acesso às ferramentas de operação (túnel SSH)

Grafana, Prometheus e o Swagger em produção só respondem a `127.0.0.1` no nginx (`allow 127.0.0.1; deny all`). O acesso é por túnel SSH — por exemplo:

```bash
ssh -L 3080:localhost:3080 SEU_USUARIO@SEU_HOST
# depois, no navegador local:
#   Grafana    → http://localhost:3080/grafana/
#   Prometheus → http://localhost:3080/prometheus/
#   Swagger    → http://localhost:3080/api/swagger
```

### Dashboards e alertas

Provisionados em `infra/grafana/`:

- **Dashboards** — `tesouro-direto.json` (métricas de app e negócio: frescor do último preço, latência, erros, simulações) e `host.json` (CPU/memória/disco/rede via node-exporter).
- **Alertas** (10 regras em `infra/grafana/provisioning/alerting/`, destino **Telegram**): dado velho (frescor > 48h útil), app down, DB/readiness down, taxa de erro 5xx alta, latência p95 alta, falha de import, simulador degradado (BCB indisponível), simulador com taxa de falhas alta, disco raiz acima de 85% e rate limit anômalo (429 na borda).

### Rotina de manutenção

Teto de log nos três acumuladores para o disco não encher: Docker (`infra/host/daemon.json`, rotação do log-driver), nginx (logrotate do pacote) e Loki (retention de 30 dias). O alerta "disco raiz acima de 85%" cobre a borda restante.

---

## Estrutura de documentação

As decisões vivem nos docs — mantenha-os como fonte de verdade e deixe este README apontando para eles.

- [`docs/PLANO.md`](docs/PLANO.md) — plano de melhorias por tarefa, cada uma com sua nota de *Feito* (o "porquê" das decisões e alternativas rejeitadas).
- [`docs/MAPA.md`](docs/MAPA.md) — mapa do sistema: rotas, modelo de dados, integrações, jobs/observabilidade e verificação de fragilidades.
- [`docs/arch/`](docs/arch/) — ADRs: caching da API ([`ARCH-001`](docs/arch/ARCH-001-api-caching.md)) e as notas de arquitetura do simulador/BCB/dias úteis (`F7a`–`F7d`).
- [`docs/analises/observabilidade.md`](docs/analises/observabilidade.md) — análise da stack de observabilidade (as 5 camadas).
- [`docs/comentarios-resgatados.md`](docs/comentarios-resgatados.md) — comentários preservados na limpeza de código.

