using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class TituloReadRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private NpgsqlDataSource _dataSource = null!;
    private TituloReadRepository _readRepository = null!;
    private TituloWriteRepository _writeRepository = null!;

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
        _readRepository = new TituloReadRepository(_dataSource);
        _writeRepository = new TituloWriteRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task SeedTitulos()
    {
        var titulos = new[]
        {
            Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value,
            Titulo.Create(TipoTitulo.TesouroIPCA, DataVencimento.Create(new DateOnly(2035, 5, 15)).Value).Value,
            Titulo.Create(TipoTitulo.TesouroPrefixado, DataVencimento.Create(new DateOnly(2020, 1, 1)).Value).Value,
            Titulo.Create(TipoTitulo.TesouroIPCAComJuros, DataVencimento.Create(new DateOnly(2040, 8, 15)).Value).Value,
        };

        foreach (var titulo in titulos)
        {
            await _writeRepository.AddAsync(titulo, CancellationToken.None);
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GetFilteredAsync_WithNoFilters_ShouldReturnAllTitulos()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync(null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetFilteredAsync_WithIndexadorFilter_ShouldReturnMatchingTitulos()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync("IPCA", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(t => t.Indexador.Should().Be("IPCA"));
    }

    [Fact]
    public async Task GetFilteredAsync_WithVencidoTrue_ShouldReturnExpiredTitulos()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync(null, true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Vencido.Should().BeTrue();
        result.Value.First().TipoTitulo.Should().Be("Tesouro Prefixado");
    }

    [Fact]
    public async Task GetFilteredAsync_WithVencidoFalse_ShouldReturnActiveTitulos()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync(null, false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().AllSatisfy(t => t.Vencido.Should().BeFalse());
    }

    [Fact]
    public async Task GetFilteredAsync_WithBothFilters_ShouldReturnMatchingTitulos()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync("IPCA", false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(t =>
        {
            t.Indexador.Should().Be("IPCA");
            t.Vencido.Should().BeFalse();
        });
    }

    [Fact]
    public async Task GetFilteredAsync_WithNoMatches_ShouldReturnEmptyCollection()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync("IGPM", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFilteredAsync_ShouldReturnCorrectDtoFields()
    {
        await SeedTitulos();

        var result = await _readRepository.GetFilteredAsync("Selic", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var titulo = result.Value.First();
        titulo.Id.Should().NotBeEmpty();
        titulo.TipoTitulo.Should().Be("Tesouro Selic");
        titulo.DataVencimento.Should().Be("2029-03-01");
        titulo.Indexador.Should().Be("Selic");
        titulo.PagaJurosSemestrais.Should().BeFalse();
        titulo.Vencido.Should().BeFalse();
    }
}
