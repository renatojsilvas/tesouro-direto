# Como ligar tudo

## 1. Copiar os arquivos

Copie a pasta `.claude/agents/` e o `CLAUDE.md` para a raiz do seu repo.
O Claude Code detecta os subagents automaticamente (se for a primeira vez
que a pasta `agents/` existe, reinicie a sessão). Confira com `/agents`.

## 2. Memória em grafo (o "segundo cérebro")

Escolha um MCP server de memória e registre. Exemplo com o memory server
oficial (grafo de conhecimento simples, roda local):

    claude mcp add memoria -- npx -y @modelcontextprotocol/server-memory

Alternativas mais robustas: basic-memory (notas em Markdown + grafo) ou um
MCP de Neo4j se você quiser consultas Cypher de verdade.

Depois, alimente: cole resumos de reuniões/decisões e peça "grave isso na
memória como entidades e relações". O CLAUDE.md já instrui o orquestrador
a consultar essa memória no início de cada tarefa.

## 3. Ativar a orquestração

Requisitos: Claude Code atualizado (v2.1.154+), plano pago. No plano Pro,
ative "Dynamic workflows" em /config.

Dois modos de uso:

- Pontual: escreva `ultracode` no prompt de uma tarefa grande
  ("ultracode: audite autenticação em toda a API e corrija os achados")
- Sessão inteira: `/effort ultracode` — o orquestrador decide sozinho
  quando abrir workflows paralelos. Volte com `/effort high`.

## 4. Controlar custo

- Rode a sessão principal no modelo forte e deixe o roteamento barato
  com os subagents (o campo `model` no frontmatter de cada um).
- Teste o fluxo primeiro num diretório pequeno antes de soltar no repo
  inteiro.
- Use `/effort ultracode` só para trabalho realmente multi-etapa; tarefa
  rotineira no modo padrão.

## 5. Teste de fumaça

1. `claude` na raiz do repo
2. `/agents` → os 4 agents aparecem
3. Peça: "ultracode: mapeie os pontos de entrada da aplicação, proponha
   e implemente melhoria de logs, com verificação adversarial"
4. Observe: plano → despacho paralelo → revisor → síntese

Docs oficiais: https://docs.claude.com/en/docs/claude-code/overview
