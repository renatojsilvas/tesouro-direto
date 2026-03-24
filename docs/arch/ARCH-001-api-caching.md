# Architecture Decision: API Caching

## Contexto

Todos os endpoints fazem queries diretas ao banco em cada request. Dados como titulos, tributos e feriados mudam raramente (diariamente ou menos), mas sao consultados em alta frequencia — especialmente pelo simulador, que busca tributos + feriados em cada chamada.

## Estrategia: IMemoryCache no Infrastructure

Usar `IMemoryCache` do .NET (in-process) por ser a opcao mais simples e suficiente para o cenario atual (single instance, dados pequenos). Redis/distributed cache seria over-engineering neste momento.

### Onde o cache vive na arquitetura

O cache e um **detalhe de infraestrutura** — nao polui Domain nem Application.

```
Application (ports)          Infrastructure (adapters)
┌─────────────────────┐     ┌──────────────────────────────┐
│ ITituloReadRepository│────▶│ CachedTituloReadRepository   │
│                     │     │   ├── IMemoryCache            │
│                     │     │   └── TituloReadRepository    │
└─────────────────────┘     └──────────────────────────────┘
```

**Padrao: Decorator** — cada `Cached*Repository` envolve o repositorio real. A Application nao sabe que cache existe.

## Componentes

### Infrastructure (novos)

| Classe | Decora | Cache Key | TTL | Invalidacao |
|--------|--------|-----------|-----|-------------|
| `CachedTituloReadRepository` | `TituloReadRepository` | `titulos:{indexador}:{vencido}` | 24h | ImportCsvCommand |
| `CachedTributoReadRepository` | `TributoReadRepository` | `tributos:all`, `tributos:ativos` | 24h | Create/UpdateTributoCommand |
| `CachedFeriadoReadRepository` | `FeriadoReadRepository` | `feriados:datas` | 7 dias | ImportFeriadosCommand |
| `CachedPrecoTaxaReadRepository` | `PrecoTaxaReadRepository` | `preco-atual:{tituloId}` | 6h | ImportCsvCommand |

### Infrastructure (alterados)

| Arquivo | Alteracao |
|---------|-----------|
| `DependencyInjection.cs` | Registrar decorators, adicionar `AddMemoryCache()` |

### Application (alterados)

| Arquivo | Alteracao |
|---------|-----------|
| `ICacheInvalidator.cs` (novo port) | Interface para invalidar cache por dominio |
| `ImportCsvCommandHandler.cs` | Chamar `ICacheInvalidator` apos import |
| `ImportFeriadosCommandHandler.cs` | Chamar `ICacheInvalidator` apos import |
| `CreateTributoCommandHandler.cs` | Chamar `ICacheInvalidator` apos create |
| `UpdateTributoCommandHandler.cs` | Chamar `ICacheInvalidator` apos update |

## Detalhamento

### 1. Port de Invalidacao (Application)

```csharp
// src/TesouroDireto.Application/Abstractions/ICacheInvalidator.cs
public interface ICacheInvalidator
{
    void InvalidateTitulos();
    void InvalidatePrecos();
    void InvalidateTributos();
    void InvalidateFeriados();
}
```

A Application conhece apenas a interface — nao sabe que e IMemoryCache por baixo.

### 2. Decorator Pattern (Infrastructure)

```csharp
// Exemplo: CachedTituloReadRepository
public sealed class CachedTituloReadRepository(
    TituloReadRepository inner,
    IMemoryCache cache) : ITituloReadRepository
{
    public async Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(
        string? indexador, bool? vencido, CancellationToken ct)
    {
        var key = $"titulos:{indexador ?? "all"}:{vencido?.ToString() ?? "all"}";

        if (cache.TryGetValue(key, out IReadOnlyCollection<TituloDto>? cached))
            return Result<IReadOnlyCollection<TituloDto>>.Success(cached!);

        var result = await inner.GetFilteredAsync(indexador, vencido, ct);

        if (result.IsSuccess)
            cache.Set(key, result.Value, TimeSpan.FromHours(24));

        return result;
    }
}
```

### 3. Implementacao do ICacheInvalidator (Infrastructure)

```csharp
public sealed class MemoryCacheInvalidator(IMemoryCache cache) : ICacheInvalidator
{
    // Usa prefixos conhecidos para remover entradas
    // IMemoryCache nao suporta wildcard nativo — manter lista de keys ativas
    // Alternativa simples: usar CancellationTokenSource por dominio
}
```

**Abordagem para invalidacao por prefixo:** usar `CancellationTokenSource` por dominio. Cada entry registrada com um token. Para invalidar, cancela o token (expira todas as entries do dominio) e cria novo token.

```csharp
public sealed class MemoryCacheInvalidator(IMemoryCache cache) : ICacheInvalidator
{
    private CancellationTokenSource _titulosCts = new();
    private CancellationTokenSource _precosCts = new();
    private CancellationTokenSource _tributosCts = new();
    private CancellationTokenSource _feriadosCts = new();

    public CancellationTokenSource GetTitulosToken() => _titulosCts;
    // ... getters para cada dominio

    public void InvalidateTitulos()
    {
        _titulosCts.Cancel();
        _titulosCts.Dispose();
        _titulosCts = new();
    }
    // ... idem para outros dominios
}
```

Os decorators usam o token ao registrar no cache:

```csharp
var options = new MemoryCacheEntryOptions()
    .SetAbsoluteExpiration(TimeSpan.FromHours(24))
    .AddExpirationToken(new CancellationChangeToken(invalidator.GetTitulosToken().Token));

cache.Set(key, result.Value, options);
```

### 4. Registro no DI

```csharp
// DependencyInjection.cs
services.AddMemoryCache();

// Registrar repos concretos como transient (internos)
services.AddScoped<TituloReadRepository>();
services.AddScoped<PrecoTaxaReadRepository>();
services.AddScoped<TributoReadRepository>();
services.AddScoped<FeriadoReadRepository>();

// Registrar decorators como implementacao das interfaces
services.AddScoped<ITituloReadRepository, CachedTituloReadRepository>();
services.AddScoped<IPrecoTaxaReadRepository, CachedPrecoTaxaReadRepository>();
services.AddScoped<ITributoReadRepository, CachedTributoReadRepository>();
services.AddScoped<IFeriadoReadRepository, CachedFeriadoReadRepository>();

// Invalidator como Singleton (mantém os CancellationTokenSources)
services.AddSingleton<MemoryCacheInvalidator>();
services.AddSingleton<ICacheInvalidator>(sp => sp.GetRequiredService<MemoryCacheInvalidator>());
```

### 5. O que NAO cachear (por enquanto)

| Endpoint | Motivo |
|----------|--------|
| `GET /titulos/{id}/precos` (historico) | Result sets grandes e variaveis (date range). Beneficio/custo baixo. |
| `POST /simulador` | Resultado depende de inputs unicos. Inputs sao combinatorios demais para cache hit razoavel. |

Se no futuro o historico de precos se tornar gargalo, considerar cache por `{tituloId}:{dataInicio}:{dataFim}` com TTL curto (1h).

## Fluxo de Dados

### Read (com cache)

```
Request → Endpoint → MediatR → Handler → CachedRepository
                                              ├── cache HIT → retorna direto
                                              └── cache MISS → Repository → DB → cache SET → retorna
```

### Write (com invalidacao)

```
Request → Endpoint → MediatR → Handler → WriteRepository → DB
                                      └── ICacheInvalidator.Invalidate*()
                                              └── CancellationTokenSource.Cancel()
                                                    └── IMemoryCache entries expiram
```

## Impacto por Endpoint

| Endpoint | Impacto Esperado |
|----------|-----------------|
| `GET /titulos` | ~30 titles, cache hit 99%+ (muda 1x/dia) |
| `GET /titulos/{id}/preco-atual` | Cache hit alto entre imports |
| `POST /simulador` | Tributos + feriados cacheados = -2 queries/request |
| `POST /simulador/cenarios` | Idem simulador |
| `GET /configuracoes/tributos` | Cache hit 99%+ (muda raramente) |

### Estimativa de reducao de queries no simulador

Antes: 4-5 queries por chamada (titulo + tributos + feriados + preco + projecao)
Depois: 1-2 queries (titulo + projecao HTTP — tributos e feriados do cache)

## Decisoes e Justificativas

| Decisao | Justificativa |
|---------|---------------|
| `IMemoryCache` (in-process) | Single instance, dados pequenos (<50KB total). Redis adicionaria complexidade de infra sem beneficio. |
| Decorator Pattern | Respeita Clean Architecture — Application nao sabe que cache existe. Facil de testar e remover. |
| `CancellationTokenSource` para invalidacao | IMemoryCache nao suporta wildcard removal. CTS e o pattern recomendado pela Microsoft. |
| TTLs conservadores (6-24h) | Dados mudam 1x/dia no maximo. TTL alto = hit rate alto. Invalidacao explicita cobre mudancas antecipadas. |
| ICacheInvalidator como port | Handlers de comando invalidam cache sem depender de IMemoryCache. Testavel com mock. |
| Nao cachear historico de precos | Result sets grandes e variaveis. Memory pressure sem hit rate suficiente. |

## Riscos

| Risco | Mitigacao |
|-------|-----------|
| Memory pressure se dados crescerem | Dados cacheados sao pequenos (<50KB). Monitorar com metricas Prometheus. TTLs garantem expiracao. |
| Stale data apos import falho | Import faz invalidacao no finally/apos sucesso. Se falhar antes, TTL expira naturalmente. |
| Cache nao compartilhado entre instancias | Aceitavel para single instance. Se escalar, migrar para Redis (mesmo ICacheInvalidator, adapter diferente). |
| Testes existentes podem quebrar | Decorators sao transparentes. Testes de integracao usam DI real — cache funciona. Testes unitarios mockam a interface (nao veem cache). |

## Ordem de Implementacao

1. `ICacheInvalidator` (port na Application)
2. `MemoryCacheInvalidator` (adapter na Infrastructure)
3. `CachedTributoReadRepository` + `CachedFeriadoReadRepository` (maior impacto — simulador)
4. `CachedTituloReadRepository`
5. `CachedPrecoTaxaReadRepository` (apenas preco-atual)
6. Invalidacao nos command handlers (Import, Create/UpdateTributo)
7. Registro no DI
8. Testes unitarios dos decorators
9. Metricas de cache hit/miss (Prometheus counter)

## Alternativas Descartadas

| Alternativa | Por que descartada |
|------------|-------------------|
| Response Caching (HTTP) | Nao reduz queries — apenas evita re-render. Nao ajuda o simulador (POST). |
| Output Caching (.NET 7+) | Similar ao response caching. Bom para GETs puros mas nao resolve queries internas do simulador. |
| Redis | Over-engineering para single instance. Adiciona dependencia de infra. |
| Cache no Handler (Application) | Viola Clean Architecture — cache e detalhe de infra. |
| Scrutor para Decorators | Dependencia extra para algo que se resolve com DI manual em 10 linhas. |
