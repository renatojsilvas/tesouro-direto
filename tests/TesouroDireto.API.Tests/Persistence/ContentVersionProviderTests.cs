using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Http;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class ContentVersionProviderTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private NpgsqlDataSource _dataSource = null!;
    private ContentVersionProvider _provider = null!;

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
        _provider = new ContentVersionProvider(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetVersionAsync_WithEmptyDatabase_ShouldReturnStableToken()
    {
        var first = await _provider.GetVersionAsync(CancellationToken.None);
        var second = await _provider.GetVersionAsync(CancellationToken.None);

        first.Should().Be(second);
    }

    [Fact]
    public async Task GetVersionAsync_AfterInsertingTitulo_ShouldChange()
    {
        var before = await _provider.GetVersionAsync(CancellationToken.None);

        var titulo = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _dbContext.Titulos.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var after = await _provider.GetVersionAsync(CancellationToken.None);

        after.Should().NotBe(before);
    }

    [Fact]
    public async Task GetVersionAsync_AfterInsertingPrecoTaxa_ShouldChange()
    {
        var titulo = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _dbContext.Titulos.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var before = await _provider.GetVersionAsync(CancellationToken.None);

        var preco = PrecoTaxa.Create(titulo.Id, DataBase.Create(new DateOnly(2024, 1, 2)).Value,
            Taxa.Create(13.12m).Value, Taxa.Create(13.18m).Value,
            PrecoUnitario.Create(756.43m).Value, PrecoUnitario.Create(755.39m).Value,
            PrecoUnitario.Create(756.43m).Value).Value;
        await _dbContext.PrecosTaxas.AddAsync(preco, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var after = await _provider.GetVersionAsync(CancellationToken.None);

        after.Should().NotBe(before);
    }
}
