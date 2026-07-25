# Seed de Tributos + Feriados (Tarefa 9) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Popular tributos (IOF/IR) e feriados automática e idempotentemente num banco novo, via seeder na aplicação (CQRS) que passa pelo domínio, sem passo manual de deploy.

**Architecture:** Um `SeedTributosCommand` (MediatR) constrói os tributos canônicos via as factories do domínio (`Tributo.Create`/`Faixa.Create`, que validam invariantes fiscais) e persiste pelos ports `ITributoWriteRepository` + `IUnitOfWork`, sendo idempotente por checagem de existência via `ITributoReadRepository`. Uma extensão `InitializeDatabaseAsync` engancha no boot (após `MigrateAsync`, fora de `Testing`): seed de tributos é **fatal** em falha; import de feriados (reuso do `ImportFeriadosCommand` da ANBIMA) roda só no primeiro boot (tabela vazia) e é **não-fatal**.

**Tech Stack:** .NET 8, MediatR 12.x, EF Core (Postgres), xUnit + FluentAssertions + NSubstitute, Testcontainers (postgres:16-alpine via `ApiTestFactory`).

**Spec:** [`docs/superpowers/specs/2026-07-22-seed-tributos-feriados-design.md`](../specs/2026-07-22-seed-tributos-feriados-design.md)

## Global Constraints

- Tipos da camada Application são `sealed` (ou `static`); commands são `record`; handlers têm **ctor único**; repos retornam `Task<Result>`/`Task<Result<T>>` — impostos por `TesouroDireto.Architecture.Tests` (rodar ao fim de tasks que tocam Application).
- Result Pattern: `Result` não-genérico usa `Result.Success()` / `Result.Failure(Error)` (não há operador implícito de `Error`). `Result<T>` tem operadores implícitos de `T` e de `Error` (pode `return valor;` ou `return error;`).
- Seed roda em Dev e Prod, **excluído em `Testing`** (mesmo guard das migrations em `Program.cs`).
- Valores fiscais canônicos = os do `tests/TesouroDireto.E2E.Tests/seed.sql` (fonte atual validada; memória `project_tributos_configurados`). Reproduzir **exatamente**.
- Testes: xUnit (`[Fact]`), FluentAssertions (`.Should()`), NSubstitute (`Substitute.For<T>()`); handlers instanciados por ctor direto (nunca via DI); integração via `ApiTestFactory` (`[Collection("api")]`, `IAsyncLifetime`, `ResetAsync()`, `SeedAsync(sp => ...)`).

---

### Task 1: `TributosPadrao` — dados canônicos construídos via domínio

**Files:**
- Create: `src/TesouroDireto.Application/Tributos/TributosPadrao.cs`
- Test: `tests/TesouroDireto.Application.Tests/Tributos/TributosPadraoTests.cs`

**Interfaces:**
- Consumes: `Tributo.Create(string, BaseCalculo, TipoCalculo, IReadOnlyCollection<Faixa>, int ordem, bool cumulativo) → Result<Tributo>`; `Faixa.Create(int? diasMin, int? diasMax, int? dia, decimal aliquota) → Result<Faixa>`.
- Produces: `static Result<IReadOnlyList<Tributo>> TributosPadrao.Build()` — devolve `[IOF, IR]` validados, ou o primeiro `Error` de construção.

- [ ] **Step 1: Write the failing test**

Create `tests/TesouroDireto.Application.Tests/Tributos/TributosPadraoTests.cs`:
```csharp
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using Xunit;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class TributosPadraoTests
{
    [Fact]
    public void Build_ShouldReturnIofAndIr_WithCanonicalValues()
    {
        var result = TributosPadrao.Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        var iof = result.Value.Single(t => t.Nome == "IOF");
        iof.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        iof.TipoCalculo.Should().Be(TipoCalculo.TabelaDiaria);
        iof.Cumulativo.Should().BeTrue();
        iof.Ordem.Should().Be(1);
        iof.Faixas.Should().HaveCount(29);
        iof.Faixas.Should().OnlyContain(f => f.DiasMin == null && f.DiasMax == null && f.Dia != null);
        iof.Faixas.Single(f => f.Dia == 1).Aliquota.Should().Be(96m);
        iof.Faixas.Single(f => f.Dia == 29).Aliquota.Should().Be(3m);

        var ir = result.Value.Single(t => t.Nome == "Imposto de Renda");
        ir.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        ir.TipoCalculo.Should().Be(TipoCalculo.FaixaPorDias);
        ir.Cumulativo.Should().BeFalse();
        ir.Ordem.Should().Be(2);
        ir.Faixas.Should().HaveCount(4);
        ir.Faixas.Should().ContainSingle(f => f.DiasMin == 0 && f.DiasMax == 180 && f.Dia == null && f.Aliquota == 22.5m);
        ir.Faixas.Should().ContainSingle(f => f.DiasMin == 721 && f.DiasMax == 999999 && f.Aliquota == 15m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TesouroDireto.Application.Tests --filter TributosPadraoTests`
Expected: FAIL — compilação falha (`TributosPadrao` não existe).

- [ ] **Step 3: Write minimal implementation**

Create `src/TesouroDireto.Application/Tributos/TributosPadrao.cs`:
```csharp
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public static class TributosPadrao
{
    // Alíquotas de IOF por dia de resgate (índice 0 = dia 1), tabela regressiva de 29 dias.
    // Fonte: tests/TesouroDireto.E2E.Tests/seed.sql (memória project_tributos_configurados).
    private static readonly decimal[] IofAliquotas =
    [
        96m, 93m, 90m, 86m, 83m, 80m, 76m, 73m, 70m, 66m,
        63m, 60m, 56m, 53m, 50m, 46m, 43m, 40m, 36m, 33m,
        30m, 26m, 23m, 20m, 16m, 13m, 10m, 6m, 3m
    ];

    public static Result<IReadOnlyList<Tributo>> Build()
    {
        var iof = BuildIof();
        if (iof.IsFailure)
        {
            return iof.Error;
        }

        var ir = BuildIr();
        if (ir.IsFailure)
        {
            return ir.Error;
        }

        return Result<IReadOnlyList<Tributo>>.Success([iof.Value, ir.Value]);
    }

    private static Result<Tributo> BuildIof()
    {
        var faixas = new List<Faixa>(IofAliquotas.Length);
        for (var i = 0; i < IofAliquotas.Length; i++)
        {
            var faixa = Faixa.Create(diasMin: null, diasMax: null, dia: i + 1, aliquota: IofAliquotas[i]);
            if (faixa.IsFailure)
            {
                return faixa.Error;
            }

            faixas.Add(faixa.Value);
        }

        return Tributo.Create("IOF", BaseCalculo.Rendimento, TipoCalculo.TabelaDiaria, faixas, ordem: 1, cumulativo: true);
    }

    private static Result<Tributo> BuildIr()
    {
        (int DiasMin, int DiasMax, decimal Aliquota)[] specs =
        [
            (0, 180, 22.5m),
            (181, 360, 20m),
            (361, 720, 17.5m),
            (721, 999_999, 15m)
        ];

        var faixas = new List<Faixa>(specs.Length);
        foreach (var (diasMin, diasMax, aliquota) in specs)
        {
            var faixa = Faixa.Create(diasMin, diasMax, dia: null, aliquota: aliquota);
            if (faixa.IsFailure)
            {
                return faixa.Error;
            }

            faixas.Add(faixa.Value);
        }

        return Tributo.Create("Imposto de Renda", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, ordem: 2, cumulativo: false);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TesouroDireto.Application.Tests --filter TributosPadraoTests`
Expected: PASS (1 test).

- [ ] **Step 5: Run Architecture.Tests (nova classe na Application)**

Run: `dotnet test tests/TesouroDireto.Architecture.Tests`
Expected: PASS (nenhuma regra violada por uma `static class`). Se falhar por convenção, ajustar conforme a mensagem antes de commitar.

- [ ] **Step 6: Commit**

```bash
git add src/TesouroDireto.Application/Tributos/TributosPadrao.cs tests/TesouroDireto.Application.Tests/Tributos/TributosPadraoTests.cs
git commit -m "feat(tributos): TributosPadrao com IOF/IR canônicos via domínio (tarefa 9)"
```

---

### Task 2: `SeedTributosCommand` + handler idempotente

**Files:**
- Create: `src/TesouroDireto.Application/Tributos/SeedTributosCommand.cs`
- Create: `src/TesouroDireto.Application/Tributos/SeedTributosCommandHandler.cs`
- Test: `tests/TesouroDireto.Application.Tests/Tributos/SeedTributosCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TributosPadrao.Build()` (Task 1); `ITributoReadRepository.GetAllAsync(CancellationToken) → Result<IReadOnlyCollection<Tributo>>`; `ITributoWriteRepository.AddAsync(Tributo, CancellationToken) → Result`; `IUnitOfWork.SaveChangesAsync(CancellationToken) → Task` (namespace `TesouroDireto.Application.Common.Interfaces`).
- Produces: `SeedTributosCommand : IRequest<Result>` e seu handler. Idempotente: no-op se já houver tributos.

- [ ] **Step 1: Write the failing test**

Create `tests/TesouroDireto.Application.Tests/Tributos/SeedTributosCommandHandlerTests.cs`:
```csharp
using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;
using Xunit;

namespace TesouroDireto.Application.Tests.Tributos;

public sealed class SeedTributosCommandHandlerTests
{
    private readonly ITributoReadRepository _readRepo = Substitute.For<ITributoReadRepository>();
    private readonly ITributoWriteRepository _writeRepo = Substitute.For<ITributoWriteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SeedTributosCommandHandler _handler;

    public SeedTributosCommandHandlerTests()
    {
        _handler = new SeedTributosCommandHandler(_readRepo, _writeRepo, _unitOfWork);
        _writeRepo.AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Handle_WhenEmpty_ShouldSeedIofAndIrAndSave()
    {
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success([]));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.Received(2).AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAlreadySeeded_ShouldBeNoOp()
    {
        var existente = TributosPadrao.Build().Value;
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Success(existente.ToList()));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenReadFails_ShouldReturnFailure()
    {
        var error = new Error("Tributo.ReadFailed", "boom");
        _readRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<Tributo>>.Failure(error));

        var result = await _handler.Handle(new SeedTributosCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<Tributo>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TesouroDireto.Application.Tests --filter SeedTributosCommandHandlerTests`
Expected: FAIL — compilação falha (`SeedTributosCommand`/handler não existem).

- [ ] **Step 3: Write the command**

Create `src/TesouroDireto.Application/Tributos/SeedTributosCommand.cs`:
```csharp
using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed record SeedTributosCommand : IRequest<Result>;
```

- [ ] **Step 4: Write the handler**

Create `src/TesouroDireto.Application/Tributos/SeedTributosCommandHandler.cs`:
```csharp
using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed class SeedTributosCommandHandler(
    ITributoReadRepository tributoReadRepository,
    ITributoWriteRepository tributoWriteRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SeedTributosCommand, Result>
{
    public async Task<Result> Handle(SeedTributosCommand request, CancellationToken cancellationToken)
    {
        var existentes = await tributoReadRepository.GetAllAsync(cancellationToken);
        if (existentes.IsFailure)
        {
            return Result.Failure(existentes.Error);
        }

        if (existentes.Value.Count > 0)
        {
            return Result.Success(); // idempotente: já semeado
        }

        var padrao = TributosPadrao.Build();
        if (padrao.IsFailure)
        {
            return Result.Failure(padrao.Error);
        }

        foreach (var tributo in padrao.Value)
        {
            var add = await tributoWriteRepository.AddAsync(tributo, cancellationToken);
            if (add.IsFailure)
            {
                return Result.Failure(add.Error);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/TesouroDireto.Application.Tests --filter SeedTributosCommandHandlerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Run Architecture.Tests**

Run: `dotnet test tests/TesouroDireto.Architecture.Tests`
Expected: PASS (command é record, handler sealed/ctor único).

- [ ] **Step 7: Commit**

```bash
git add src/TesouroDireto.Application/Tributos/SeedTributosCommand.cs src/TesouroDireto.Application/Tributos/SeedTributosCommandHandler.cs tests/TesouroDireto.Application.Tests/Tributos/SeedTributosCommandHandlerTests.cs
git commit -m "feat(tributos): SeedTributosCommand idempotente via ports (tarefa 9)"
```

---

### Task 3: Teste de integração do seed (Postgres real, idempotência)

**Files:**
- Test: `tests/TesouroDireto.API.Tests/Integration/SeedTributosIntegrationTests.cs`

**Interfaces:**
- Consumes: `SeedTributosCommand` (Task 2) via `ISender`; `ApiTestFactory` (`ResetAsync`, `SeedAsync(Func<IServiceProvider, Task>)`); `AppDbContext` (namespace de persistência — copiar dos `using` de um teste vizinho em `Integration/`, ex.: `SimuladorEndpointsTests.cs`).
- Produces: garantia de que o seed persiste IOF/IR + faixas corretas e é idempotente contra banco real.

- [ ] **Step 1: Write the failing test**

Create `tests/TesouroDireto.API.Tests/Integration/SeedTributosIntegrationTests.cs`:
```csharp
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Persistence;
using Xunit;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SeedTributosIntegrationTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seed_OnEmptyDatabase_PersistsIofAndIrWithFaixas()
    {
        await factory.SeedAsync(async sp =>
        {
            var sender = sp.GetRequiredService<ISender>();
            (await sender.Send(new SeedTributosCommand())).IsSuccess.Should().BeTrue();
        });

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var tributos = await db.Set<Tributo>().ToListAsync();

            tributos.Should().HaveCount(2);
            var iof = tributos.Single(t => t.Nome == "IOF");
            iof.Faixas.Should().HaveCount(29);
            iof.Cumulativo.Should().BeTrue();
            var ir = tributos.Single(t => t.Nome == "Imposto de Renda");
            ir.Faixas.Should().HaveCount(4);
            ir.Faixas.Should().ContainSingle(f => f.DiasMin == 0 && f.DiasMax == 180 && f.Aliquota == 22.5m);
        });
    }

    [Fact]
    public async Task Seed_RunTwice_IsIdempotent()
    {
        await factory.SeedAsync(async sp =>
        {
            var sender = sp.GetRequiredService<ISender>();
            await sender.Send(new SeedTributosCommand());
            await sender.Send(new SeedTributosCommand());
        });

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Set<Tributo>().CountAsync()).Should().Be(2);
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TesouroDireto.API.Tests --filter SeedTributosIntegrationTests`
Expected: FAIL — compilação falha até a Task 2 estar mergeada (o `SeedTributosCommand` precisa existir). Se as Tasks 1-2 já estão no branch, o teste **roda** e pode falhar só por asserção.

> Nota: em `Testing` o boot NÃO roda o seed automaticamente (guard). Por isso o teste dispara `SeedTributosCommand` explicitamente via `ISender` — cobrindo handler + write repo + DbContext + Postgres real.

- [ ] **Step 3: Ajustar imports até passar (sem mudar produção)**

Se falhar por asserção de valor, comparar com `TributosPadrao`/`seed.sql` e corrigir a ASSERÇÃO (a implementação da Task 1/2 é a fonte). Não alterar código de produção aqui.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TesouroDireto.API.Tests --filter SeedTributosIntegrationTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run full API suite (sem regressão)**

Run: `dotnet test tests/TesouroDireto.API.Tests`
Expected: PASS — 132/132 (130 anteriores + 2 novos).

- [ ] **Step 6: Commit**

```bash
git add tests/TesouroDireto.API.Tests/Integration/SeedTributosIntegrationTests.cs
git commit -m "test(tributos): integração do seed idempotente com Postgres real (tarefa 9)"
```

---

### Task 4: Wiring no boot — `InitializeDatabaseAsync` (seed fatal + feriados não-fatal)

**Files:**
- Create: `src/TesouroDireto.API/Extensions/DatabaseInitializerExtensions.cs`
- Modify: `src/TesouroDireto.API/Program.cs` (substituir o bloco `if (!IsEnvironment("Testing")) { using scope; MigrateAsync }` por `await app.InitializeDatabaseAsync();`)

**Interfaces:**
- Consumes: `SeedTributosCommand` (Task 2), `ImportFeriadosCommand` (`TesouroDireto.Application.Feriados`, existente, `IRequest<Result<ImportFeriadosResult>>`), `IFeriadoReadRepository.GetAllDatasAsync(CancellationToken) → Result<IReadOnlyCollection<DateOnly>>`, `AppDbContext`, `ISender`, `ILogger`.
- Produces: `static Task InitializeDatabaseAsync(this WebApplication app)` que, fora de `Testing`: migra, semeia tributos (fatal em falha) e importa feriados só no 1º boot (não-fatal).

- [ ] **Step 1: Escrever a extensão**

Create `src/TesouroDireto.API/Extensions/DatabaseInitializerExtensions.cs`:
```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Extensions;

public static class DatabaseInitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<Program>>();

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var sender = sp.GetRequiredService<ISender>();

        // Tributos: FATAL em falha (valores canônicos/DB quebrados = deploy quebrado).
        var seed = await sender.Send(new SeedTributosCommand());
        if (seed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seed de tributos falhou no boot: {seed.Error.Code} {seed.Error.Description}");
        }

        // Feriados: só no primeiro boot (tabela vazia). NÃO-FATAL (tarefa 10/Quartz refaz).
        var feriadoRead = sp.GetRequiredService<IFeriadoReadRepository>();
        var datas = await feriadoRead.GetAllDatasAsync(CancellationToken.None);
        if (datas.IsSuccess && datas.Value.Count == 0)
        {
            var import = await sender.Send(new ImportFeriadosCommand());
            if (import.IsFailure)
            {
                logger.LogWarning(
                    "Import de feriados no primeiro boot falhou (seguindo sem abortar): {Code} {Description}",
                    import.Error.Code, import.Error.Description);
            }
        }
    }
}
```

- [ ] **Step 2: Alterar o `Program.cs`**

Em `src/TesouroDireto.API/Program.cs`, localizar o bloco:
```csharp
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```
Substituir por:
```csharp
await app.InitializeDatabaseAsync();
```
Adicionar `using TesouroDireto.API.Extensions;` no topo se ainda não existir. Remover `using`s que fiquem órfãos (ex.: do `AppDbContext`/EF, se não forem usados em outro ponto do `Program.cs`).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: 0 erros, 0 avisos.

- [ ] **Step 4: Suíte de integração (boot em `Testing` não quebrou)**

Run: `dotnet test tests/TesouroDireto.API.Tests`
Expected: PASS — 132/132 (a extensão retorna cedo em `Testing`; nenhum teste regride).

- [ ] **Step 5: Verificação manual (critério de aceite do plano)**

Requer Docker + `.env` com `API_KEY` != `dev-local-key` (tarefa 19) e ANBIMA acessível. Executar:
```bash
docker compose down -v            # descarta o volume (banco novo)
docker compose up -d --build
# aguardar boot; então (ajuste porta/host conforme compose):
curl -s -X POST http://localhost:5000/simulador \
  -H "X-Api-Key: <chave-real>" -H "Content-Type: application/json" \
  -d '{"tituloNome":"Tesouro Selic 2029","valorInvestido":1000,"dataAplicacao":"2024-01-02","dataResgate":"2025-01-02"}'
```
Expected: HTTP 200 com resultado de simulação (não erro de tributo/feriado ausente). Conferir nos logs (Loki/console CLEF) a linha do `SeedTributosCommand` e, se a tabela estava vazia, do `ImportFeriadosCommand`. Se a ANBIMA estiver fora, feriados podem faltar (não-fatal, esperado) — registrar no relatório.

- [ ] **Step 6: Commit**

```bash
git add src/TesouroDireto.API/Extensions/DatabaseInitializerExtensions.cs src/TesouroDireto.API/Program.cs
git commit -m "feat(api): InitializeDatabaseAsync — seed de tributos + feriados no boot (tarefa 9)"
```

---

## Verificação final (após as 4 tasks)

- [ ] `dotnet test` (solução inteira) verde.
- [ ] `dotnet test tests/TesouroDireto.Architecture.Tests` verde.
- [ ] Verificação manual da Task 4 executada e resultado real reportado.
- [ ] Marcar a tarefa 9 como concluída no `docs/PLANO.md` (índice + bloco `> Feito:`) e atualizar `docs/MAPA.md` (Verificação #4 e §4 "Sem seed" → RESOLVIDO) + memória (`project_tributos_configurados` continua válida; criar status da tarefa 9). Atualizar a tabela de Verificação: item 4 (seed) → ✔️ RESOLVIDO.
