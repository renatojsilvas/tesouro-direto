using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class TributoRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private ITributoWriteRepository _writeRepo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AppDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync(CancellationToken.None);

        _writeRepo = new TesouroDireto.Infrastructure.Persistence.Repositories.TributoWriteRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistTributoWithFaixas()
    {
        var faixas = new[]
        {
            Faixa.Create(0, 180, null, 22.5m).Value,
            Faixa.Create(181, 360, null, 20m).Value,
            Faixa.Create(361, 720, null, 17.5m).Value,
            Faixa.Create(721, null, null, 15m).Value
        };

        var tributo = Tributo.Create("Imposto de Renda", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        await _writeRepo.AddAsync(tributo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.Tributos
            .FirstOrDefaultAsync(t => t.Id == tributo.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Nome.Should().Be("Imposto de Renda");
        found.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        found.TipoCalculo.Should().Be(TipoCalculo.FaixaPorDias);
        found.Faixas.Should().HaveCount(4);
        found.Ativo.Should().BeTrue();
        found.Ordem.Should().Be(1);
        found.Cumulativo.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistChanges()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IOF", BaseCalculo.Rendimento, TipoCalculo.TabelaDiaria, faixas, 2, false).Value;

        await _writeRepo.AddAsync(tributo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        tributo.Desativar();
        _writeRepo.Update(tributo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.Tributos
            .FirstOrDefaultAsync(t => t.Id == tributo.Id, CancellationToken.None);

        found!.Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task GetAtivosOrdenados_ShouldReturnOnlyActiveOrderedByOrdem()
    {
        var f = new[] { Faixa.Create(0, 180, null, 22.5m).Value };

        var t1 = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, f, 2, false).Value;
        var t2 = Tributo.Create("IOF", BaseCalculo.Rendimento, TipoCalculo.TabelaDiaria, f, 1, false).Value;
        var t3 = Tributo.Create("Inativo", BaseCalculo.PuBruto, TipoCalculo.AliquotaFixa, f, 3, false).Value;
        t3.Desativar();

        await _dbContext.Tributos.AddRangeAsync(new[] { t1, t2, t3 }, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();

        var readRepo = new TesouroDireto.Infrastructure.Persistence.Repositories.TributoReadRepository(_dbContext);
        var ativos = await readRepo.GetAtivosOrdenadosAsync(CancellationToken.None);

        ativos.Should().HaveCount(2);
        ativos.First().Nome.Should().Be("IOF");
        ativos.Last().Nome.Should().Be("IR");
    }
}
