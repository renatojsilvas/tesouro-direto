using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Integration;

/// <summary>
/// Fábrica de host real (Program.cs) para testes de integração HTTP end-to-end,
/// apoiada em um Postgres efêmero via Testcontainers.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ValidApiKey = "integration-test-api-key-0123456789";

    // Program.cs é um Minimal Hosting API: `builder.Services.AddInfrastructure(builder.Configuration)`
    // roda de forma síncrona/imediata ANTES de `builder.Build()`, e é ali que a
    // connection string é lida (NpgsqlDataSource.Create + AddDbContext). Overrides via
    // WebApplicationFactory.ConfigureWebHost/ConfigureAppConfiguration só passam a valer
    // DEPOIS desse ponto (funcionam bem para leituras pós-Build, como ApiKey:Key lido
    // pelo middleware a cada request) — então não chegam a tempo para a connection
    // string. Variáveis de ambiente do processo, por outro lado, já existem quando
    // `WebApplication.CreateBuilder(args)` monta a configuração inicial (a fonte
    // "Environment Variables" é adicionada bem cedo), então são a única forma
    // confiável de sobrescrever valores lidos antes do Build().
    private const string CsvImportUrlEnvVar = "CsvImport__Url";
    private const string FeriadoImportUrlEnvVar = "FeriadoImport__Url";
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string ApiKeyEnvVar = "ApiKey__Key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// Overrides de config (tarefa 13 — resiliência) aplicados por cima dos defaults de
    /// teste abaixo (CacheTtl curto etc.) em <see cref="ConfigureWebHost"/>. Usado por
    /// testes que precisam de host PRÓPRIO (não a fixture "api" compartilhada, injetada
    /// via <see cref="ApiCollection"/>) — ex.: o teste de circuit breaker do BCB, que
    /// precisa de um <c>MinimumThroughput</c> baixo, deliberadamente frágil, e não pode
    /// arriscar vazar um circuito aberto para os outros ~10 arquivos de teste que também
    /// usam o BCB através da fixture compartilhada.
    ///
    /// Propriedade (não parâmetro de construtor) de propósito: xUnit exige um único
    /// construtor público em tipos usados como <c>ICollectionFixture</c> e o instancia
    /// via reflection sem parâmetros — precisa ficar sem overloads. Setar via
    /// object initializer ANTES de <see cref="InitializeAsync"/> (o host só é
    /// efetivamente construído no primeiro acesso a <c>Services</c>, dentro dele).
    /// </summary>
    public IReadOnlyDictionary<string, string?> ConfigOverrides { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>
    /// Responder do BCB Focus injetável por teste (tarefa 11). Plugado como o
    /// <see cref="HttpMessageHandler"/> primário do <see cref="FocusBcbService"/> via
    /// <see cref="ConfigureWebHost"/> — cada teste que precisa simular o BCB seta esta
    /// propriedade antes de disparar a request. <c>null</c> (estado após
    /// <see cref="ResetAsync"/>) faz o handler lançar, para nunca disparar uma chamada de
    /// rede real por acidente num teste que esqueceu de configurar o responder.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? BcbResponder { get; set; }

    /// <summary>
    /// Relógio fake compartilhado pela fixture (Time.Testing), injetado DIRETAMENTE no
    /// <see cref="CachedProjecaoMercadoService"/> montado em <see cref="ConfigureWebHost"/>
    /// (e no <see cref="ISystemClock"/> do <see cref="IMemoryCache"/> via
    /// <see cref="FakeSystemClock"/>) — permite testar expiração de TTL do cache de
    /// projeção avançando o tempo de forma determinística com <see cref="AdvanceTime"/>,
    /// sem <c>Task.Delay</c> real. Só avança (forward-only) e é compartilhado entre todos
    /// os testes da collection "api"; como cada teste que usa TTL aquece o cache DEPOIS do
    /// <see cref="ResetAsync"/> (que invalida por token) e avança um delta pequeno e
    /// conhecido, não há vazamento de estado entre testes.
    ///
    /// IMPORTANTE: propositalmente NÃO substitui o <see cref="TimeProvider"/> global do
    /// DI (via <c>services.AddSingleton&lt;TimeProvider&gt;</c>) — comprovado por teste que
    /// isso vaza para o pipeline de resiliência do BCB Focus (<c>AddResilienceHandler</c>
    /// resolve <see cref="TimeProvider"/> do container para agendar os delays de
    /// retry/circuit breaker), travando qualquer teste que dependa de um retry com delay
    /// real até estourar o <c>HttpClient.Timeout</c> de 30s (o delay do Polly some fica
    /// esperando um <see cref="TimeProvider"/> congelado que nunca avança sozinho). Por
    /// isso o relógio fake só chega ao decorator de cache via injeção direta abaixo, não
    /// pelo slot genérico de <see cref="TimeProvider"/> do container.
    /// </summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Avança <see cref="Time"/> em <paramref name="delta"/> — usado por testes que
    /// precisam expirar entradas do cache de projeção sem esperar wall-clock real.
    /// </summary>
    public void AdvanceTime(TimeSpan delta) => Time.Advance(delta);

    public async Task InitializeAsync()
    {
        // xUnit só chama DisposeAsync se InitializeAsync completar com sucesso — se
        // StartAsync/MigrateAsync falhar no meio, precisamos limpar as env vars nós
        // mesmos aqui (senão elas vazam para o resto do processo de teste), antes de
        // deixar a exceção propagar.
        try
        {
            await _postgres.StartAsync();

            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, ValidApiKey);

            // appsettings.json traz URLs reais (tesourotransparente.gov.br / anbima.com.br).
            // Sem sobrescrever aqui, os testes de importação disparariam download real e
            // travariam. Um único espaço passa por IsNullOrWhiteSpace nos serviços de
            // importação (forçando o branch de "URL não configurada", sem I/O externo) —
            // string vazia não serve porque Environment.SetEnvironmentVariable remove a
            // variável quando o valor é "".
            Environment.SetEnvironmentVariable(CsvImportUrlEnvVar, " ");
            Environment.SetEnvironmentVariable(FeriadoImportUrlEnvVar, " ");

            // Acessar Services força a construção do host (as env vars acima já precisam
            // estar setadas nesse ponto, pois AddInfrastructure lê a connection string
            // de forma síncrona antes do Build()).
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Program.cs pula a migração automática sob o environment "Testing" —
            // aqui rodamos manualmente antes de qualquer teste usar o host.
            await db.Database.MigrateAsync();
        }
        catch
        {
            ClearEnvironmentVariables();

            // Se o container chegou a subir, tenta descartá-lo também; não deixa a
            // falha da limpeza mascarar a exceção original.
            try
            {
                await _postgres.DisposeAsync();
            }
            catch
            {
                // ignora erro de limpeza — a exceção original é a relevante.
            }

            throw;
        }
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await _postgres.DisposeAsync();
        }
        finally
        {
            ClearEnvironmentVariables();
            await base.DisposeAsync();
        }
    }

    private static void ClearEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, null);
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, null);
        Environment.SetEnvironmentVariable(CsvImportUrlEnvVar, null);
        Environment.SetEnvironmentVariable(FeriadoImportUrlEnvVar, null);
    }

    /// <summary>
    /// Client HTTP com o header X-Api-Key já preenchido com uma chave válida.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, ValidApiKey);
        return client;
    }

    /// <summary>
    /// Abre um scope de DI e executa a ação de seed (ex.: via write repositories ou
    /// diretamente no AppDbContext). Quem chama é responsável por dar SaveChanges
    /// quando necessário (os write repositories deste projeto não commitam sozinhos).
    /// </summary>
    public async Task SeedAsync(Func<IServiceProvider, Task> seed)
    {
        using var scope = Services.CreateScope();
        await seed(scope.ServiceProvider);
    }

    /// <summary>
    /// Limpa todas as tabelas de domínio e invalida o cache em memória (os read
    /// repositories cacheados por 6-24h não seriam invalidados pelo TRUNCATE, já
    /// que a invalidação normal só ocorre via CacheInvalidationBehavior em cima de
    /// commands do MediatR, que o seed direto via repositório não passa por).
    /// </summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "titulos", "precos_taxas", "tributos", "feriados" RESTART IDENTITY CASCADE""");

        var invalidator = scope.ServiceProvider.GetRequiredService<MemoryCacheInvalidator>();
        invalidator.InvalidateTitulos();
        invalidator.InvalidatePrecos();
        invalidator.InvalidateTributos();
        invalidator.InvalidateFeriados();
        invalidator.InvalidateProjecoes();

        // Isolamento entre testes: sem BcbResponder configurado, o BcbResponderHandler
        // lança em vez de deixar um teste anterior "vazar" resposta para o próximo.
        BcbResponder = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // FocusBcb:CacheTtl é lido pelo CachedProjecaoMercadoService a cada chamada
            // (via IConfiguration, não capturado antes do Build()), então dá para
            // encurtar só no host de teste sem o truque de env var usado para a
            // connection string. TTL curto permite testar expiração sem esperar 6h de
            // verdade; MaxFallbackAge fica no default de produção (7 dias).
            //
            // Resiliência (tarefa 13): retry do BCB com delay quase zero (testes rápidos) e
            // circuit breaker com MinimumThroughput alto o bastante para nunca abrir por
            // acidente nos ~10 arquivos de teste que compartilham esta fixture — o resto
            // dos parâmetros fica nos defaults de produção definidos em
            // AddFocusBcbResilienceHandler. ConfigOverrides (setado só por hosts isolados
            // como o teste de circuit breaker, via object initializer) vence em caso de
            // colisão de chave.
            var defaults = new Dictionary<string, string?>
            {
                ["FocusBcb:CacheTtl"] = "00:00:02",
                ["Resilience:FocusBcb:Retry:BaseDelay"] = "00:00:00.050",
                ["Resilience:FocusBcb:CircuitBreaker:MinimumThroughput"] = "1000"
            };

            foreach (var (key, value) in ConfigOverrides)
            {
                defaults[key] = value;
            }

            config.AddInMemoryCollection(defaults);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<FocusBcbService>()
                .ConfigurePrimaryHttpMessageHandler(() => new BcbResponderHandler(() => BcbResponder));

            // Relógio fake (Time — ver doc acima) alimenta o IMemoryCache (via
            // FakeSystemClock) e é injetado DIRETAMENTE no CachedProjecaoMercadoService
            // abaixo — de propósito, NÃO via slot genérico de TimeProvider do container
            // (vazaria para o Polly do BCB Focus, travando testes de retry/circuit
            // breaker — ver doc de Time).
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache>(
                _ => new MemoryCache(new MemoryCacheOptions { Clock = new FakeSystemClock(Time) }));

            services.RemoveAll<IProjecaoMercadoService>();
            services.AddScoped<IProjecaoMercadoService>(sp => new CachedProjecaoMercadoService(
                sp.GetRequiredService<FocusBcbService>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>(),
                Time,
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<CachedProjecaoMercadoService>>()));
        });
    }

    /// <summary>
    /// Microsoft.Extensions.Caching.Memory ainda não expõe TimeProvider em
    /// MemoryCacheOptions — só o ISystemClock legado (Clock). Adapter fininho que faz o
    /// MemoryCache seguir o mesmo relógio fake (Time) do TimeProvider do host, para que
    /// SetAbsoluteExpiration(TimeSpan) expire de forma determinística ao avançar o tempo.
    /// </summary>
    private sealed class FakeSystemClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }

    private sealed class BcbResponderHandler(Func<Func<HttpRequestMessage, HttpResponseMessage>?> responderAccessor)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responder = responderAccessor()
                ?? throw new InvalidOperationException(
                    "ApiTestFactory.BcbResponder não foi configurado para este teste.");

            return Task.FromResult(responder(request));
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiTestFactory>;
