using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.Persistence;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
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
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiTestFactory>;
