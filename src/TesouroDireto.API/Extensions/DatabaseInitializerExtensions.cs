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
