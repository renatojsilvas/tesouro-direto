# Review Agent - Checklist Obrigatorio

✅ OK | ❌ FALHA | ⚠️ ATENCAO | ➖ N/A

## BLOCO 1: RESULT PATTERN
1.1 Domain nao lanca excecao para negocio
1.2 Application nao lanca excecao para negocio
1.3 Metodos que falham retornam Result/Result<T>
1.4 **Repositorios retornam Result** — TODOS os metodos (AddAsync, Remove, GetById, GetAll)
1.5 **Infrastructure nunca re-throw** — nem OperationCanceledException. Retorna Result.Failure
1.6 Erros sao static readonly
1.7 Error.Code segue {Entidade}.{Motivo}
1.8 Handler verifica cada Result (fail-fast)
1.9 Resultado mapeado para HTTP status

## BLOCO 2: VALUE OBJECTS
2.1 VOs sao **records**
2.2 Construtor privado
2.3 Factory method retorna Result<T>
2.4 Validacoes no VO
2.5 IDs tipados (records)
2.6 Commands recebem primitivos, conversao no Handler

## BLOCO 3: CLEAN ARCHITECTURE (PORTS & ADAPTERS)
3.1 Domain **SEM interfaces** — puro
3.2 Interfaces (ports) na **Application**
3.3 Implementacoes (adapters) na Infrastructure
3.4 API referencia Application + Infrastructure (so DI)
3.5 Handler orquestra, logica no dominio

## BLOCO 4: CQRS + DATA ACCESS
4.1 Read/Write repos **separados**
4.2 WriteOnlyRepository usa **EF Core** e trabalha com **entidades** (nao DTOs)
4.3 ReadOnlyRepository usa **Dapper** e retorna **DTOs/records** (nao entidades)
4.4 Dapper queries usam `CommandDefinition` com `cancellationToken` propagado
4.5 Retornos de colecao sao `IReadOnlyCollection<T>` (nunca `List<T>`)
4.6 Commands mutam, Queries leem
4.7 Retornam Result/Result<T>
4.8 Um Handler por Command/Query
4.9 ISender nos endpoints (nao IMediator)

## BLOCO 5: MINIMAL API
5.1 Nenhum Controller
5.2 Extension methods + MapGroup()
5.3 Request DTOs sao records
5.4 ResultExtensions.ToHttpResult()
5.5 Status codes corretos
5.6 CancellationToken **sempre explicito, nunca default**

## BLOCO 6: EF CORE
6.1 EntityTypeConfiguration por entidade
6.2 VOs mapeados com HasConversion
6.3 UnitOfWork implementa SaveChangesAsync
6.4 Connection string em configuracao

## BLOCO 7: QUALIDADE C#
7.1 sealed em classes nao herdadas
7.2 Records para VOs/DTOs/Commands/Queries/Errors
7.3 Construtores primarios em Handlers/Services
7.4 Async/await com CancellationToken **obrigatorio em toda a stack**
7.5 **Nenhum** `CancellationToken ct = default` ou `CancellationToken cancellationToken = default` **em lugar nenhum** do codebase
7.6 CancellationToken propagado em **todas** as chamadas async (repo, mediator, dapper, ef core)
7.7 Nullable reference types tratados
7.8 Uma classe por arquivo
7.9 Sem codigo morto
7.10 **Warnings sao erros** — `TreatWarningsAsErrors=true` em Directory.Build.props
7.11 Build compila com zero warnings (sao erros)
7.12 **Sem over-engineering**

## BLOCO 8: TESTES
8.1 Unitarios existem
8.2 Integracao existem (Testcontainers)
8.3 E2E existem para features com UI (Playwright)
8.4 Piramide respeitada
8.5 Should_{Behavior}_When_{Condition}
8.6 Mocks so em unitarios
8.7 Validam Result, nao Assert.Throws
8.8 **Feature:** E2E tests derivam dos criterios de aceite do PM
8.9 **Feature:** entrega valor ponta a ponta (back + front testados juntos)
8.10 **Testes de arquitetura** usam reflection/scan por convencao no assembly — nunca lista manual de tipos
8.11 **E2E executados**, nao so escritos — teste sem execucao = documentacao morta
8.12 **Foundation:** E2E existentes continuam verdes (nao cria novos, mas valida)
8.13 dotnet test passa
8.14 npx playwright test passa (se tem E2E)

## BLOCO 9: FRONTEND — Blazor (se aplicavel)
9.1 API separada do frontend
9.2 Componentes reutilizaveis
9.3 Tratamento de loading / error / empty states
9.4 Tipagem forte nos models
9.5 Minimal JS interop

## BLOCO 10: OBSERVABILIDADE
10.1 Serilog configurado com logs estruturados
10.2 Logs enviados para Grafana (Loki)
10.3 **Correlation ID** propagado em todas as requisicoes
10.4 Correlation ID presente nos logs (LogContext)
10.5 Metricas expostas para Grafana (Prometheus)
10.6 Nao tem log desnecessario (nem de menos, nem de mais)

## BLOCO 11: DESIGN PHILOSOPHY
11.1 **Sem over-engineering** — complexidade justificada
11.2 Deep modules: interface simples, implementacao rica
11.3 Cada abstracao reduz complexidade (nao adiciona)
11.4 Codigo e simples o suficiente, nao simplista

## BLOCO 12: SEGURANCA (sanity check rapido — deep dive e no SEC agent)
12.1 Sem SQL concatenado — Dapper usa parametros
12.2 Sem secrets hardcoded (connection strings, tokens, senhas)
12.3 Request/Response usam DTOs (nao expoem entidades de dominio)
12.4 Stack traces nao expostos em respostas de producao

## BLOCO 13: ANALISE ESTATICA
13.1 `dotnet list package --vulnerable` sem vulnerabilidades
13.2 SonarQube configurado (sonar-project.properties existe)
13.3 Sonar roda no CI (GitHub Actions)
13.4 Sem code smells criticos no Sonar

## BLOCO 14: DEPLOY
14.1 Dockerfile existe e funciona
14.2 docker-compose.yml com app + db + observabilidade + sonar
14.3 GitHub Actions CI/CD existe
14.4 Testes rodam no CI
14.5 SonarQube scan roda no CI

Score X/10. ❌ = codigo corrigido. Score < 7 = bloqueia.

$ARGUMENTS
