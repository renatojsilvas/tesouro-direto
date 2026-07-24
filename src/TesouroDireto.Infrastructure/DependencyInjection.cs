using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz;
using TesouroDireto.Application.Common.Behaviors;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Infrastructure.Caching;
using TesouroDireto.Infrastructure.CsvImport;
using TesouroDireto.Infrastructure.Feriados;
using TesouroDireto.Infrastructure.Observability;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Registra config estática do Dapper (name matching + DateOnly) uma única vez,
        // no boot, antes de qualquer repositório ser resolvido — ver DapperTypeHandlers.
        DapperTypeHandlers.Register();

        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddMemoryCache();
        services.AddSingleton<MemoryCacheInvalidator>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        services.AddScoped<ITituloWriteRepository, TituloWriteRepository>();
        services.AddScoped<TituloReadRepository>();
        services.AddScoped<ITituloReadRepository>(sp =>
            new CachedTituloReadRepository(
                sp.GetRequiredService<TituloReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>()));

        services.AddScoped<IPrecoTaxaWriteRepository, PrecoTaxaWriteRepository>();
        services.AddScoped<PrecoTaxaReadRepository>();
        services.AddScoped<IPrecoTaxaReadRepository>(sp =>
            new CachedPrecoTaxaReadRepository(
                sp.GetRequiredService<PrecoTaxaReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>()));

        services.AddScoped<ITributoWriteRepository, TributoWriteRepository>();
        services.AddScoped<TributoReadRepository>();
        services.AddScoped<ITributoReadRepository>(sp =>
            new CachedTributoReadRepository(
                sp.GetRequiredService<TributoReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>()));

        services.AddScoped<IFeriadoWriteRepository, FeriadoWriteRepository>();
        services.AddScoped<FeriadoReadRepository>();
        services.AddScoped<IFeriadoReadRepository>(sp =>
            new CachedFeriadoReadRepository(
                sp.GetRequiredService<FeriadoReadRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<MemoryCacheInvalidator>()));

        services.AddScoped<IDiasUteisService, DiasUteisService>();

        services.AddHttpClient<ICsvImportService, CsvImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHttpClient<IFeriadoImportService, FeriadoImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // FocusBcbService.cs deixa de ser exposto diretamente como IProjecaoMercadoService:
        // o decorator de cache (CachedProjecaoMercadoService, tarefa 11) é quem responde
        // por IProjecaoMercadoService, com FocusBcbService como colaborador interno.
        services.AddHttpClient<FocusBcbService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

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

        return services;
    }
}
