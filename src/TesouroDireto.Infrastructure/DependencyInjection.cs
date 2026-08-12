using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Prometheus;
using Quartz;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Infrastructure.ApiKeys;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.CsvImport;
using TesouroDireto.Infrastructure.Feriados;
using TesouroDireto.Infrastructure.Http;
using TesouroDireto.Infrastructure.Observability;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;
using TesouroDireto.Infrastructure.Projecoes;
using TesouroDireto.Infrastructure.RateLimiting;

namespace TesouroDireto.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Teto de conexões do pool ÚNICO do Npgsql (EF Core + Dapper compartilham o mesmo
    /// <see cref="NpgsqlDataSource"/>). Antes da 74.2 existiam DUAS pools da mesma
    /// connection string (uma do EF, uma do <see cref="NpgsqlDataSource"/> usado pelos
    /// read repositories via Dapper), cada uma capaz de crescer até o default do Npgsql
    /// (100) — 200 conexões possíveis contra um Postgres que precisa caber em ~288 MB
    /// (tarefa 74.3) e que passará a hospedar bancos de OUTRAS aplicações na mesma VPS.
    /// Colapsar em uma pool só já corta o pior caso pela metade; o valor abaixo corta
    /// mais, porque a pool única atende TANTO EF quanto Dapper sem duplicar a mesma
    /// necessidade de conexão simultânea, e porque o `app` roda sob CPU muito apertada
    /// (0,15–0,18 vCPU na 74.3): sob esse teto de CPU, o número real de conexões
    /// SIMULTANEAMENTE em uso é baixo — o gargalo é CPU, não I/O — então um pool grande
    /// só manteria conexões ociosas abertas no Postgres, gastando a memória mais escassa
    /// do orçamento. Cada conexão ociosa custa memória privada no backend do Postgres
    /// (a ordem de grandeza usada no dimensionamento do `db` em `docs/PLANO.md`, 74.2,
    /// é ~4 MB/conexão) — 10 conexões ≈ 40 MB do lado do Postgres, contra os até 80 MB
    /// que duas pools de 10 cada já reservavam antes. `NoResetOnClose = true` (abaixo)
    /// já evita o custo de RESET a cada devolução à pool, então uma pool menor não perde
    /// throughput por reconexão — só reduz o pico de conexões abertas ociosas.
    /// </summary>
    private const int NpgsqlMaxPoolSize = 10;

    /// <summary>
    /// Teto de "unidades" do <see cref="IMemoryCache"/> — cada entrada custa exatamente
    /// 1 unidade (ver <c>MemoryCacheResultExtensions.GetOrCreateResultAsync</c>,
    /// <c>CachedProjecaoMercadoService</c> e <c>CachedContentVersionProvider</c>, os três
    /// pontos que escrevem no cache). "1 por entrada" limita CARDINALIDADE (nº de chaves
    /// distintas), não bytes — decisão deliberada: os 6 decorators de cache desta camada
    /// têm cardinalidade naturalmente pequena e ligada ao catálogo de domínio, não a
    /// entrada de usuário — Titulos (~15 combos de indexador×vencido + 2 chaves por
    /// título do catálogo), Tributos (2 chaves fixas), Feriados (1 chave), Projeções
    /// (2 chaves por indexador, 4 indexadores = 8), ContentVersion (1 chave) e ApiKeys
    /// (uma por hash de chave ATIVA — só entra no cache em sucesso, nunca em falha de
    /// autenticação, então tentativas de adivinhação por um atacante não inflam a
    /// cardinalidade). Nenhum desses caches é chaveado por texto livre de requisição.
    /// Diante disso, medir/estimar bytes por entrada teria custo de implementação (e CPU
    /// em runtime) sem ganho real de precisão: o número de chaves possíveis já é pequeno
    /// e limitado pelo catálogo, não pela carga. O limite abaixo é deliberadamente
    /// generoso — muito acima do total realista de chaves somando os 6 decorators — e
    /// funciona como rede de segurança contra regressão futura (um decorator novo, ou uma
    /// mudança que passe a cachear por parâmetro de requisição), não como orçamento de
    /// bytes: 10.000 entradas de tamanho 1 nunca é atingido pelo uso atual, mas barra
    /// crescimento sem limite se alguém violar essa premissa sem perceber.
    /// </summary>
    private const long MemoryCacheSizeLimit = 10_000;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperTypeHandlers.Register();

        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection")!)
        {
            NoResetOnClose = true,
            MaxPoolSize = NpgsqlMaxPoolSize
        }.ConnectionString;

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddMemoryCache(options => options.SizeLimit = MemoryCacheSizeLimit);
        services.AddSingleton<MemoryCacheInvalidator>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
        services.AddSingleton<IBusinessMetrics, BusinessMetrics>();
        services.AddSingleton<IApiKeyMetrics, ApiKeyMetrics>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        services.AddScoped<ITituloWriteRepository, TituloWriteRepository>();
        services.AddScoped<TituloReadRepository>();
        services.AddScoped<ITituloReadRepository>(sp =>
            new CachedTituloReadRepository(
                sp.GetRequiredService<TituloReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddScoped<IPrecoTaxaWriteRepository, PrecoTaxaWriteRepository>();
        services.AddScoped<PrecoTaxaReadRepository>();
        services.AddScoped<IPrecoTaxaReadRepository>(sp =>
            new CachedPrecoTaxaReadRepository(
                sp.GetRequiredService<PrecoTaxaReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddScoped<ITributoWriteRepository, TributoWriteRepository>();
        services.AddScoped<TributoReadRepository>();
        services.AddScoped<ITributoReadRepository>(sp =>
            new CachedTributoReadRepository(
                sp.GetRequiredService<TributoReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddScoped<IFeriadoWriteRepository, FeriadoWriteRepository>();
        services.AddScoped<FeriadoReadRepository>();
        services.AddScoped<IFeriadoReadRepository>(sp =>
            new CachedFeriadoReadRepository(
                sp.GetRequiredService<FeriadoReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddScoped<IUsuarioWriteRepository, UsuarioWriteRepository>();
        services.AddScoped<IUsuarioReadRepository, UsuarioReadRepository>();
        services.AddScoped<IApiKeyWriteRepository, ApiKeyWriteRepository>();
        services.AddScoped<ApiKeyReadRepository>();
        services.AddScoped<IApiKeyReadRepository>(sp =>
            new CachedApiKeyReadRepository(
                sp.GetRequiredService<ApiKeyReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();

        services.AddScoped<IDiasUteisService, DiasUteisService>();

        services.AddScoped<SimuladorApplicationService>();

        services.AddScoped<ContentVersionProvider>();
        services.AddScoped<IContentVersionProvider>(sp =>
            new CachedContentVersionProvider(
                sp.GetRequiredService<ContentVersionProvider>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConfiguration>()));

        services.AddHttpClient<ICsvImportService, CsvImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        })
        .UseHttpClientMetrics()
        .AddBatchImportResilienceHandler(configuration, "csv-import-resilience", "Resilience:CsvImport");

        services.AddHttpClient<IFeriadoImportService, FeriadoImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .UseHttpClientMetrics()
        .AddBatchImportResilienceHandler(configuration, "feriado-import-resilience", "Resilience:FeriadoImport");

        services.AddHttpClient<FocusBcbService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .UseHttpClientMetrics()
        .AddFocusBcbResilienceHandler(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IProjecaoMercadoService>(sp =>
            new CachedProjecaoMercadoService(
                sp.GetRequiredService<FocusBcbService>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<CachedProjecaoMercadoService>>()));

        var cronSchedule = configuration["CsvImport:CronSchedule"] ?? "0 0 6 * * ?";
        var feriadosCronSchedule = configuration["Feriados:CronSchedule"] ?? "0 0 6 1 12 ?";

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("csv-import");
            q.AddJob<CsvImportJob>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("csv-import-trigger")
                .WithCronSchedule(cronSchedule));

            var feriadoJobKey = new JobKey("feriado-import");
            q.AddJob<FeriadoImportJob>(opts => opts.WithIdentity(feriadoJobKey));
            q.AddTrigger(opts => opts
                .ForJob(feriadoJobKey)
                .WithIdentity("feriado-import-trigger")
                .WithCronSchedule(feriadosCronSchedule));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var permitLimit = config.GetValue<int?>("RateLimiting:PermitLimit") ?? 60;
            var windowSeconds = config.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
            return new RateLimitingOptions(permitLimit, TimeSpan.FromSeconds(windowSeconds));
        });

        services.AddSingleton<IRateLimitStore>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var store = config["RateLimiting:Store"] ?? "InMemory";

            return string.Equals(store, "Redis", StringComparison.OrdinalIgnoreCase)
                ? new RedisRateLimitStore()
                : new InMemoryRateLimitStore(sp.GetRequiredService<RateLimitingOptions>());
        });

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var permitLimit = config.GetValue<int?>("RateLimiting:AuthFailure:PermitLimit") ?? 10;
            var windowSeconds = config.GetValue<int?>("RateLimiting:AuthFailure:WindowSeconds") ?? 60;
            return new AuthFailureRateLimitingOptions(permitLimit, TimeSpan.FromSeconds(windowSeconds));
        });

        services.AddSingleton<IAuthFailureRateLimiter>(sp =>
            new InMemoryAuthFailureRateLimiter(sp.GetRequiredService<AuthFailureRateLimitingOptions>()));

        return services;
    }

    public static IHttpResiliencePipelineBuilder AddFocusBcbResilienceHandler(
        this IHttpClientBuilder builder, IConfiguration configuration)
    {
        return builder.AddResilienceHandler("focus-bcb-resilience", pipeline =>
        {
            var section = configuration.GetSection("Resilience:FocusBcb");

            var totalTimeout = section.GetValue<TimeSpan?>("TotalTimeout") ?? TimeSpan.FromSeconds(25);
            var retryMaxAttempts = section.GetValue<int?>("Retry:MaxAttempts") ?? 2;
            var retryBaseDelay = section.GetValue<TimeSpan?>("Retry:BaseDelay") ?? TimeSpan.FromSeconds(0.5);
            var failureRatio = section.GetValue<double?>("CircuitBreaker:FailureRatio") ?? 0.5;
            var minimumThroughput = section.GetValue<int?>("CircuitBreaker:MinimumThroughput") ?? 10;
            var samplingDuration = section.GetValue<TimeSpan?>("CircuitBreaker:SamplingDuration") ?? TimeSpan.FromSeconds(30);
            var breakDuration = section.GetValue<TimeSpan?>("CircuitBreaker:BreakDuration") ?? TimeSpan.FromSeconds(15);
            var attemptTimeout = section.GetValue<TimeSpan?>("AttemptTimeout") ?? TimeSpan.FromSeconds(6);

            pipeline
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = totalTimeout })
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = retryBaseDelay,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = failureRatio,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = samplingDuration,
                    BreakDuration = breakDuration,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = attemptTimeout });
        });
    }

    public static IHttpResiliencePipelineBuilder AddBatchImportResilienceHandler(
        this IHttpClientBuilder builder, IConfiguration configuration, string pipelineName, string configSection)
    {
        return builder.AddResilienceHandler(pipelineName, pipeline =>
        {
            var section = configuration.GetSection(configSection);

            var retryMaxAttempts = section.GetValue<int?>("Retry:MaxAttempts") ?? 3;
            var retryBaseDelay = section.GetValue<TimeSpan?>("Retry:BaseDelay") ?? TimeSpan.FromSeconds(2);
            var attemptTimeout = section.GetValue<TimeSpan?>("AttemptTimeout") ?? TimeSpan.FromSeconds(45);

            pipeline
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = retryBaseDelay,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = attemptTimeout });
        });
    }
}
