using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Feriados;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Titulo> Titulos => Set<Titulo>();
    public DbSet<PrecoTaxa> PrecosTaxas => Set<PrecoTaxa>();
    public DbSet<Tributo> Tributos => Set<Tributo>();
    public DbSet<Feriado> Feriados => Set<Feriado>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }
}
