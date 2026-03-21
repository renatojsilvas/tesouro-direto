using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Titulo> Titulos => Set<Titulo>();
    public DbSet<PrecoTaxa> PrecosTaxas => Set<PrecoTaxa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }
}
