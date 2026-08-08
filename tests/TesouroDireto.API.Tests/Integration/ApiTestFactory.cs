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

public class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ValidApiKey = "integration-test-api-key-0123456789";

    private const string CsvImportUrlEnvVar = "CsvImport__Url";
    private const string FeriadoImportUrlEnvVar = "FeriadoImport__Url";
    private const string ConnectionStringEnvVar = "ConnectionStrings__DefaultConnection";
    private const string ApiKeyEnvVar = "ApiKey__Key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public IReadOnlyDictionary<string, string?> ConfigOverrides { get; init; } =
        new Dictionary<string, string?>();

    public Func<HttpRequestMessage, HttpResponseMessage>? BcbResponder { get; set; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void AdvanceTime(TimeSpan delta) => Time.Advance(delta);

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();

            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _postgres.GetConnectionString());
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, ValidApiKey);

            Environment.SetEnvironmentVariable(CsvImportUrlEnvVar, " ");
            Environment.SetEnvironmentVariable(FeriadoImportUrlEnvVar, " ");

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.Database.MigrateAsync();
        }
        catch
        {
            ClearEnvironmentVariables();

            try
            {
                await _postgres.DisposeAsync();
            }
            catch
            {
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

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, ValidApiKey);
        return client;
    }

    public async Task SeedAsync(Func<IServiceProvider, Task> seed)
    {
        using var scope = Services.CreateScope();
        await seed(scope.ServiceProvider);
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "titulos", "precos_taxas", "tributos", "feriados", "usuarios", "api_keys" RESTART IDENTITY CASCADE""");

        var invalidator = scope.ServiceProvider.GetRequiredService<MemoryCacheInvalidator>();
        invalidator.InvalidateTitulos();
        invalidator.InvalidatePrecos();
        invalidator.InvalidateTributos();
        invalidator.InvalidateFeriados();
        invalidator.InvalidateProjecoes();
        invalidator.InvalidateApiKeys();

        BcbResponder = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
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
