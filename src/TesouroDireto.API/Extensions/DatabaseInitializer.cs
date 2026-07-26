using MediatR;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Extensions;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken ct = default);
}

public sealed class EfCoreDatabaseMigrator(AppDbContext db) : IDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var migrator = sp.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(ct);

        var sender = sp.GetRequiredService<ISender>();

        var seed = await sender.Send(new SeedTributosCommand(), ct);
        if (seed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seed de tributos falhou no boot: {seed.Error.Code} {seed.Error.Description}");
        }

        var feriadoRead = sp.GetRequiredService<IFeriadoReadRepository>();
        var datas = await feriadoRead.GetAllDatasAsync(ct);
        if (datas.IsFailure)
        {
            logger.LogWarning(
                "Checagem de feriados no boot falhou (seguindo sem importar): {Code} {Description}",
                datas.Error.Code, datas.Error.Description);
        }
        else if (datas.Value.Count == 0)
        {
            var import = await sender.Send(new ImportFeriadosCommand(), ct);
            if (import.IsFailure)
            {
                logger.LogWarning(
                    "Import de feriados no primeiro boot falhou (seguindo sem abortar): {Code} {Description}",
                    import.Error.Code, import.Error.Description);
            }
        }
    }
}
