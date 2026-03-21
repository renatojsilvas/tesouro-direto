using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class PrecoTaxaReadRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private NpgsqlDataSource _dataSource = null!;
    private PrecoTaxaReadRepository _readRepository = null!;
    private Titulo _titulo = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var connectionString = _postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync(CancellationToken.None);

        _dataSource = NpgsqlDataSource.Create(connectionString);
        _readRepository = new PrecoTaxaReadRepository(_dataSource);

        _titulo = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;

        await _dbContext.Titulos.AddAsync(_titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task SeedPrecos()
    {
        var precos = new[]
        {
            PrecoTaxa.Create(_titulo.Id, DataBase.Create(new DateOnly(2024, 1, 2)).Value,
                Taxa.Create(13.12m), Taxa.Create(13.18m),
                PrecoUnitario.Create(756.43m).Value, PrecoUnitario.Create(755.39m).Value,
                PrecoUnitario.Create(756.43m).Value).Value,
            PrecoTaxa.Create(_titulo.Id, DataBase.Create(new DateOnly(2024, 6, 15)).Value,
                Taxa.Create(10.50m), Taxa.Create(10.75m),
                PrecoUnitario.Create(800.00m).Value, PrecoUnitario.Create(799.00m).Value,
                PrecoUnitario.Create(798.00m).Value).Value,
            PrecoTaxa.Create(_titulo.Id, DataBase.Create(new DateOnly(2024, 12, 20)).Value,
                Taxa.Create(11.00m), Taxa.Create(11.25m),
                PrecoUnitario.Create(850.00m).Value, PrecoUnitario.Create(849.00m).Value,
                PrecoUnitario.Create(848.00m).Value).Value,
        };

        await _dbContext.PrecosTaxas.AddRangeAsync(precos, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TituloExistsAsync_WhenExists_ShouldReturnTrue()
    {
        var result = await _readRepository.TituloExistsAsync(_titulo.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task TituloExistsAsync_WhenNotExists_ShouldReturnFalse()
    {
        var result = await _readRepository.TituloExistsAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task GetByTituloIdAsync_WithNoFilters_ShouldReturnAll()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(_titulo.Id, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByTituloIdAsync_WithDataInicio_ShouldFilterFromDate()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(
            _titulo.Id, new DateOnly(2024, 6, 1), null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByTituloIdAsync_WithDataFim_ShouldFilterToDate()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(
            _titulo.Id, null, new DateOnly(2024, 6, 30), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByTituloIdAsync_WithBothFilters_ShouldFilterPeriod()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(
            _titulo.Id, new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().DataBase.Should().Be("2024-06-15");
    }

    [Fact]
    public async Task GetByTituloIdAsync_ShouldReturnCorrectDtoFields()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(
            _titulo.Id, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var dto = result.Value.First();
        dto.Id.Should().NotBeEmpty();
        dto.DataBase.Should().Be("2024-01-02");
        dto.TaxaCompra.Should().Be(13.12m);
        dto.TaxaVenda.Should().Be(13.18m);
        dto.PuCompra.Should().Be(756.43m);
        dto.PuVenda.Should().Be(755.39m);
        dto.PuBase.Should().Be(756.43m);
    }

    [Fact]
    public async Task GetByTituloIdAsync_ShouldOrderByDataBaseAscending()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByTituloIdAsync(_titulo.Id, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dates = result.Value.Select(p => p.DataBase).ToList();
        dates.Should().BeInAscendingOrder();
    }
}
