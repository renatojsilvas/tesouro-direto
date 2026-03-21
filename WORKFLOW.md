# Guia de Workflow — TesouroDireto

## Setup Inicial

```bash
# 1. Rodar o setup (cria solution, projetos, pacotes, Dockerfile, docker-compose, CI/CD, .gitignore)
chmod +x setup.sh
./setup.sh

# 2. Subir a infra (db + observabilidade — docker-compose.yml foi criado pelo setup.sh)
docker compose up -d

# 3. Abrir o Claude Code
claude
```

Grafana disponivel em: http://localhost:3000 (admin/admin)

## Como Usar

Dois modos:

```
# Foundation — base tecnica (sem entrega de valor visivel)
/pipeline foundation Configurar Result e Error base
/pipeline foundation Configurar Serilog com Correlation ID

# Feature — entrega valor ponta a ponta (back + front)
/pipeline Listar titulos do Tesouro Direto com filtro por indexador
```

**Foundations primeiro.** Depois, cada feature entrega valor pro usuario.
Se a feature for grande, o pipeline decompoe. Se tiver duvida, ele pergunta.

## Ordem Sugerida

### Foundations (base tecnica — rodar primeiro)

| # | Prompt |
|---|--------|
| F1 | `/pipeline foundation Configurar Result, Error e classes base do dominio` |
| F2 | `/pipeline foundation Configurar Serilog com Correlation ID` |
| F3 | `/pipeline foundation Configurar API Key middleware` |
| F4 | `/pipeline foundation Criar entidade Titulo com VOs (TipoTitulo, Indexador, DataVencimento) e persistencia` |
| F5 | `/pipeline foundation Criar entidade PrecoTaxa com VOs (Taxa, PrecoUnitario, DataBase) e persistencia` |
| F6 | `/pipeline foundation Criar entidade Tributo com VOs (BaseCalculo, TipoCalculo, Faixa) e persistencia — modelo generico para IR, IOF, custos` |
| F7 | `/pipeline foundation Criar job de importacao do CSV do Tesouro Transparente (parsear CSV com separador ;, datas DD/MM/YYYY, decimais com virgula, upsert de Titulos e PrecoTaxa)` |

### Features (entregam valor — cada uma e back + front)

| # | Prompt |
|---|--------|
| 1 | `/pipeline Listar titulos disponiveis com filtro por indexador e status (ativo/vencido)` |
| 2 | `/pipeline Consultar preco e taxa atual de um titulo` |
| 3 | `/pipeline Consultar historico de precos e taxas de um titulo com filtro por periodo` |
| 4 | `/pipeline Simular investimento em um titulo (valor, data compra, taxa contratada) com calculo de rendimento bruto, tributos e rendimento liquido` |
| 5 | `/pipeline Simular investimento com cenarios futuros de taxa e indexador` |
| 6 | `/pipeline Gerenciar tributos — listar, criar e atualizar configuracoes de impostos e custos` |
| 7 | `/pipeline Dashboard com lista de titulos, precos atuais e filtros` |
| 8 | `/pipeline Tela do simulador de investimentos com formulario e resultados detalhados` |

## Agents Individuais

| Comando | Quando usar |
|---------|-------------|
| `/pm [feature]` | Escrever user story isolada |
| `/spec [feature]` | So a spec |
| `/test [feature]` | Gerar testes isolado |
| `/architect [feature]` | Planejar arquitetura |
| `/backend [feature]` | Implementar .NET |
| `/frontend [feature]` | Implementar Blazor |
| `/review [feature]` | Revisar codigo existente |
| `/sec [feature]` | Review de seguranca |
| `/qa [feature]` | Validar comportamento |
| `/commit [feature]` | Commitar manualmente |

## Deploy

O projeto ja vem com:
- **Dockerfile** multi-stage (build + runtime)
- **docker-compose.yml** com app + PostgreSQL + Grafana + Loki + Prometheus + SonarQube
- **GitHub Actions** (`deploy.yml`) que roda testes, SonarQube scan e faz deploy via SSH
- **Serilog** com logs estruturados → Grafana/Loki
- **Correlation ID** em todas as requisicoes
- **SonarQube** analise estatica local e no CI

Para deploy na VPS, configure os secrets no GitHub:
- `VPS_HOST` — IP da VPS
- `VPS_USER` — usuario SSH
- `VPS_SSH_KEY` — chave privada SSH

## Dicas

**Fale como usuario.** Voce foca no *o que*, agents resolvem o *como*.
**Features pequenas.** Um endpoint ou uma tela por vez.
**Na duvida, o agent pergunta.** Ele nunca presume.
