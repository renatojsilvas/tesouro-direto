# TesouroDireto

API de precos atuais e historicos do Tesouro Direto com simulador de investimentos e interface Blazor.

## Stack

**Backend:** .NET 8 Minimal API, Clean Architecture (Ports & Adapters), CQRS — Dapper (queries) + EF Core (commands), MediatR 12.5.0, Result Pattern, PostgreSQL, Serilog
**Frontend:** Blazor Server com API separada
**Observabilidade:** Serilog → Grafana/Loki, Metricas → Prometheus, Correlation ID
**Qualidade:** TDD, SonarQube, SEC review
**Infra:** Docker, GitHub Actions CI/CD → VPS

## Estrutura

```
src/
├── TesouroDireto.Domain/          # Entities, Value Objects (records), Result, Errors — SEM interfaces
├── TesouroDireto.Application/     # Commands, Queries, Handlers, DTOs, Interfaces (ports)
├── TesouroDireto.Infrastructure/  # EF Core, Dapper, Serilog, CSV Import (adapters)
├── TesouroDireto.API/
└── TesouroDireto.Web/             # Blazor Server
tests/
├── TesouroDireto.Domain.Tests/
├── TesouroDireto.Application.Tests/
├── TesouroDireto.API.Tests/       # Testcontainers
└── TesouroDireto.E2E.Tests/       # Playwright
```

## Regras (detalhe nos agents)

- **Clean Architecture Ports & Adapters** — Domain puro (sem interfaces), ports na Application, adapters na Infrastructure
- **Result Pattern** — nunca throw em Domain/Application. Repos retornam `Result<T>`. Infrastructure nunca re-throw.
- **Value Objects como records** — nunca primitivos, factory method retorna `Result<T>`
- **CQRS** — Commands (EF Core) + Queries (Dapper), Read/Write repos separados
- **Minimal API** — nunca Controllers, `ISender` nos endpoints
- **`IReadOnlyCollection<T>`** — nunca `List<T>`
- **`CancellationToken` obrigatorio** — nunca default
- **`TreatWarningsAsErrors`** — warnings sao erros de build
- **`sealed`** em tudo que nao sera herdado, **records** pra VOs/DTOs/Commands/Queries/Errors
- **Construtores primarios** em Handlers/Services
- **Observabilidade** — Serilog estruturado, Correlation ID propagado, metricas Prometheus
- **Seguranca** — SEC agent revisa cada entrega, SonarQube no CI
- **TDD** — RED → GREEN → REFACTOR, piramide (unit > integration > E2E). E2E e gate de todo commit.
- **Testes de arquitetura** — por reflection/scan de assembly, nunca lista manual
- **A Philosophy of Software Design** — deep modules, reduzir complexidade
- **Funcional a cada passo** — cada etapa entregue deve funcionar: banco criado, migrations aplicadas, endpoint testavel, tela navegavel. Sem mocks, sem stubs.
- **Nunca over-engineer** — KISS, YAGNI
- **Na duvida, perguntar** — nunca presumir

## Convencoes

**C#:** sealed, records, construtores primarios, async/await + CancellationToken obrigatorio, um arquivo por classe, namespaces = pastas, nomes em ingles, IReadOnlyCollection, warnings sao erros.

**Git:** Conventional Commits. Commits atomicos.

## Dominio

### Fonte de Dados
- CSV do Tesouro Transparente: https://www.tesourotransparente.gov.br/ckan/dataset/df56aa42-484a-4a59-8184-7676580c81e3/resource/796d2059-14e9-44e3-80c9-2d9e30b405c1/download/precotaxatesourodireto.csv
- Separador: `;` | Formato datas: `DD/MM/YYYY` | Decimais com `,`
- Colunas: Tipo Titulo, Data Vencimento, Data Base, Taxa Compra Manha, Taxa Venda Manha, PU Compra Manha, PU Venda Manha, PU Base Manha
- Dados desde dez/2004, periodicidade diaria

### Entidades

**Titulo** — titulo do Tesouro Direto
- `TipoTitulo` (VO) — Tesouro Selic, Prefixado, Prefixado c/ Juros Semestrais, IPCA+, IPCA+ c/ Juros Semestrais, IGP-M+ c/ Juros Semestrais
- `DataVencimento` (VO)
- `Indexador` (VO) — derivado do tipo: Selic, Prefixado, IPCA, IGPM
- `PagaJurosSemestrais` — derivado do tipo

**PrecoTaxa** — registro diario por titulo (snapshot de mercado)
- `DataBase` (VO) — data de referencia
- `TaxaCompra` (VO) — taxa % para comprar (2 decimais)
- `TaxaVenda` (VO) — taxa % para revender ao TN (sempre > compra)
- `PuCompra` (VO) — preco unitario na taxa de compra, liquidacao D+1
- `PuVenda` (VO) — preco unitario na taxa de venda, liquidacao D+1
- `PuBase` (VO) — preco unitario na taxa de venda, liquidacao D0 (marcacao a mercado)

**Tributo** — entidade generica e configuravel para impostos/custos
- `Nome` — ex: "Imposto de Renda", "IOF", "Taxa B3"
- `BaseCalculo` — enum: Rendimento, PuBruto, ValorInvestido, ValorResgate
- `TipoCalculo` — enum: FaixaPorDias, AliquotaFixa, TabelaDiaria
- `Faixas[]` — regras: DiasMin?, DiasMax?, Dia?, Aliquota
- `Ativo` — pode desativar sem deletar
- `Ordem` — sequencia de aplicacao
- `Cumulativo` — resultado afeta base do proximo?

### Servico de Dominio: Simulador

Inputs: titulo, valor investido, data compra, taxa contratada, cenario futuro (opcional)

Calculos:
1. Rendimento bruto (baseado no tipo de titulo, taxa, prazo)
2. Itera tributos ativos ordenados por Ordem
3. Para cada tributo: calcula base conforme BaseCalculo, aplica regra conforme TipoCalculo
4. Se cumulativo: ajusta base para proximo tributo
5. Resultado: rendimento bruto, lista de tributos aplicados com valores, rendimento liquido

Cenarios futuros: projecao com taxas/indexadores hipoteticos

### Autenticacao
- API Key via header `X-Api-Key`
- Simples, sem JWT

### Endpoints

| Endpoint | Descricao |
|----------|-----------|
| `GET /titulos` | Lista titulos (filtro por indexador, status) |
| `GET /titulos/{id}/precos` | Historico de precos/taxas (filtro por periodo) |
| `GET /titulos/{id}/preco-atual` | Preco/taxa mais recente |
| `POST /simulador` | Simula investimento |
| `POST /simulador/cenarios` | Simula com cenarios futuros |
| `GET /configuracoes/tributos` | Lista tributos configurados |
| `PUT /configuracoes/tributos/{id}` | Atualiza tributo |
| `POST /configuracoes/tributos` | Cria novo tributo |

### Job de Importacao
- Baixa CSV do Tesouro Transparente
- Parseia (separador `;`, datas `DD/MM/YYYY`, decimais `,`)
- Upsert: cria Titulos novos, insere PrecoTaxa
- Importacao inicial: CSV completo (~20 anos de dados)
- Atualizacao: diaria (pode ser agendada ou manual)

## Agents

| Comando | Proposito |
|---------|-----------|
| `/pipeline foundation [...]` | **Foundation:** Spec → Test → Backend → Review+SEC → Corrections → Test+Commit |
| `/pipeline [...]` | **Feature:** PM → E2E → Spec → Test → Backend → Frontend → Review+SEC → QA → Corrections → Test+Commit |
| `/architect` | System design (standalone — no pipeline e absorvido pelo Spec) |
| `/pm` | User story + criterios de aceite |
| `/spec` | Spec tecnica + decisoes arquiteturais |
| `/test` | Testes (TDD) |
| `/backend` | Implementacao .NET |
| `/frontend` | Implementacao Blazor |
| `/review` | Checklist tecnico (65+ itens) |
| `/sec` | Review de seguranca |
| `/qa` | Validacao comportamental |
| `/commit` | Conventional Commits |

**Foundations primeiro, features depois.** Cada passo deve estar funcional — sem mocks, sem stubs. Na duvida, perguntar.
