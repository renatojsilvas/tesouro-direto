using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class PrecoTaxaRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private IPrecoTaxaWriteRepository _repository = null!;
    private Titulo _titulo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AppDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync(CancellationToken.None);

        _titulo = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;

        await _dbContext.Titulos.AddAsync(_titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        _repository = new TesouroDireto.Infrastructure.Persistence.Repositories.PrecoTaxaWriteRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistPrecoTaxa()
    {
        var preco = PrecoTaxa.Create(
            _titulo.Id,
            DataBase.Create(new DateOnly(2024, 6, 15)).Value,
            Taxa.Create(10.50m).Value,
            Taxa.Create(10.75m).Value,
            PrecoUnitario.Create(1000.12m).Value,
            PrecoUnitario.Create(999.65m).Value,
            PrecoUnitario.Create(998.11m).Value).Value;

        await _repository.AddAsync(preco, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var found = await _dbContext.PrecosTaxas
            .FirstOrDefaultAsync(p => p.Id == preco.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.TituloId.Should().Be(_titulo.Id);
        found.TaxaCompra.Value.Should().Be(10.50m);
        found.PuBase.Value.Should().Be(998.11m);
    }

    [Fact]
    public async Task ExistsAsync_WhenExists_ShouldReturnTrue()
    {
        var dataBase = DataBase.Create(new DateOnly(2024, 7, 1)).Value;

        var preco = PrecoTaxa.Create(
            _titulo.Id, dataBase,
            Taxa.Create(5m).Value, Taxa.Create(6m).Value,
            PrecoUnitario.Create(500m).Value, PrecoUnitario.Create(499m).Value,
            PrecoUnitario.Create(498m).Value).Value;

        await _repository.AddAsync(preco, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var exists = await _repository.ExistsAsync(_titulo.Id, dataBase, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenNotExists_ShouldReturnFalse()
    {
        var exists = await _repository.ExistsAsync(
            _titulo.Id,
            DataBase.Create(new DateOnly(2099, 1, 1)).Value,
            CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_DuplicateTituloAndDataBase_ShouldThrowOnSave()
    {
        var dataBase = DataBase.Create(new DateOnly(2024, 8, 1)).Value;

        var p1 = PrecoTaxa.Create(
            _titulo.Id, dataBase,
            Taxa.Create(5m).Value, Taxa.Create(6m).Value,
            PrecoUnitario.Create(500m).Value, PrecoUnitario.Create(499m).Value,
            PrecoUnitario.Create(498m).Value).Value;

        var p2 = PrecoTaxa.Create(
            _titulo.Id, dataBase,
            Taxa.Create(7m).Value, Taxa.Create(8m).Value,
            PrecoUnitario.Create(600m).Value, PrecoUnitario.Create(599m).Value,
            PrecoUnitario.Create(598m).Value).Value;

        await _repository.AddAsync(p1, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        await _repository.AddAsync(p2, CancellationToken.None);

        var act = () => _dbContext.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AddRangeAsync_ShouldPersistMultipleRecords()
    {
        var precos = new[]
        {
            PrecoTaxa.Create(
                _titulo.Id, DataBase.Create(new DateOnly(2024, 9, 1)).Value,
                Taxa.Create(5m).Value, Taxa.Create(6m).Value,
                PrecoUnitario.Create(500m).Value, PrecoUnitario.Create(499m).Value,
                PrecoUnitario.Create(498m).Value).Value,
            PrecoTaxa.Create(
                _titulo.Id, DataBase.Create(new DateOnly(2024, 9, 2)).Value,
                Taxa.Create(5.1m).Value, Taxa.Create(6.1m).Value,
                PrecoUnitario.Create(501m).Value, PrecoUnitario.Create(500m).Value,
                PrecoUnitario.Create(499m).Value).Value
        };

        await _repository.AddRangeAsync(precos, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var count = await _dbContext.PrecosTaxas.CountAsync(CancellationToken.None);

        count.Should().Be(2);
    }
}
