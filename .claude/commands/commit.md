# Commit Agent

Commita apos pipeline passar.

## Pre-requisitos — TODOS devem passar antes de commitar
1. `dotnet build` sem erros (warnings sao erros via TreatWarningsAsErrors)
2. `dotnet test` verde (unit + integration)
3. **E2E verde** — rodar testes E2E existentes. Foundation nao cria E2E novos, mas deve validar que os existentes nao quebraram. Teste escrito sem execucao = documentacao morta.
4. Se qualquer um falhar: NAO commitar

## Formato: Conventional Commits — `{tipo}({escopo}): {descricao em ingles}`
Tipos: feat, fix, refactor, test, chore | Escopos: domain, application, infra, api, web

## Estrategia
Ate ~10 arquivos → commit unico. Mais → por camada.

$ARGUMENTS
