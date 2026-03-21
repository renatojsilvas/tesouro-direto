using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quartz;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Infrastructure.CsvImport;
using TesouroDireto.Infrastructure.Feriados;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;

namespace TesouroDireto.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<ITituloWriteRepository, TituloWriteRepository>();
        services.AddScoped<ITituloReadRepository, TituloReadRepository>();
        services.AddScoped<IPrecoTaxaWriteRepository, PrecoTaxaWriteRepository>();
        services.AddScoped<IPrecoTaxaReadRepository, PrecoTaxaReadRepository>();
        services.AddScoped<ITributoWriteRepository, TributoWriteRepository>();
        services.AddScoped<ITributoReadRepository, TributoReadRepository>();
        services.AddScoped<IFeriadoWriteRepository, FeriadoWriteRepository>();
        services.AddScoped<IFeriadoReadRepository, FeriadoReadRepository>();
        services.AddScoped<IDiasUteisService, DiasUteisService>();

        services.AddHttpClient<ICsvImportService, CsvImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHttpClient<IFeriadoImportService, FeriadoImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        var cronSchedule = configuration["CsvImport:CronSchedule"] ?? "0 0 6 * * ?";

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("csv-import");
            q.AddJob<CsvImportJob>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("csv-import-trigger")
                .WithCronSchedule(cronSchedule));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
