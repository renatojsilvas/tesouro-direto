using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class TituloRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private ITituloWriteRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync(CancellationToken.None);

        _repository = new TesouroDireto.Infrastructure.Persistence.Repositories.TituloWriteRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistTitulo()
    {
        var titulo = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;

        await _repository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var found = await _dbContext.Titulos
            .FirstOrDefaultAsync(t => t.Id == titulo.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.TipoTitulo.Should().Be(TipoTitulo.TesouroSelic);
        found.DataVencimento.Value.Should().Be(new DateOnly(2029, 3, 1));
        found.Indexador.Should().Be(Indexador.Selic);
        found.PagaJurosSemestrais.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WhenTituloExists_ShouldReturnTrue()
    {
        var tipo = TipoTitulo.TesouroPrefixado;
        var vencimento = DataVencimento.Create(new DateOnly(2027, 1, 1)).Value;
        var titulo = Titulo.Create(tipo, vencimento).Value;

        await _repository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var exists = await _repository.ExistsAsync(tipo, vencimento, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenTituloDoesNotExist_ShouldReturnFalse()
    {
        var exists = await _repository.ExistsAsync(
            TipoTitulo.TesouroIPCA,
            DataVencimento.Create(new DateOnly(2035, 5, 15)).Value,
            CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_DuplicateTipoAndVencimento_ShouldThrowOnSave()
    {
        var tipo = TipoTitulo.TesouroIPCA;
        var vencimento = DataVencimento.Create(new DateOnly(2030, 8, 15)).Value;

        var titulo1 = Titulo.Create(tipo, vencimento).Value;
        var titulo2 = Titulo.Create(tipo, vencimento).Value;

        await _repository.AddAsync(titulo1, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        await _repository.AddAsync(titulo2, CancellationToken.None);

        var act = () => _dbContext.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
