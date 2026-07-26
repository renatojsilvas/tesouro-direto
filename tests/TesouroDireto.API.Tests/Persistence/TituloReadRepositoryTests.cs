using Dapper;
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


    [Fact]
    public async Task GetByNomeAsync_WithExactMatch_ShouldReturnMatchingTitulo()
    {
        var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _writeRepository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByNomeAsync("Tesouro Selic 2029", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be("Tesouro Selic");
        result.Value.DataVencimento.Should().Be("2029-03-01");
        result.Value.Indexador.Should().Be("Selic");
    }

    [Fact]
    public async Task GetByNomeAsync_WithMixedCaseAndSurroundingWhitespace_ShouldReturnMatchingTitulo()
    {
        var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _writeRepository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByNomeAsync("  tesouro selic 2029  ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be("Tesouro Selic");
        result.Value.DataVencimento.Should().Be("2029-03-01");
        result.Value.Indexador.Should().Be("Selic");
    }

    [Fact]
    public async Task GetByNomeAsync_WhenNomeDoesNotExist_ShouldReturnNotFound()
    {
        var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _writeRepository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByNomeAsync("Tesouro Selic 2099", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TituloErrors.NotFound);
    }

    [Fact]
    public async Task GetByNomeAsync_WithDuplicateNameFromDifferentVencimentos_ShouldReturnOneMatch()
    {
        var titulo1 = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2031, 3, 1)).Value).Value;
        var titulo2 = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2031, 9, 1)).Value).Value;
        await _writeRepository.AddAsync(titulo1, CancellationToken.None);
        await _writeRepository.AddAsync(titulo2, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByNomeAsync("Tesouro Selic 2031", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TipoTitulo.Should().Be("Tesouro Selic");
        result.Value.DataVencimento.Should().BeOneOf("2031-03-01", "2031-09-01");
    }

    [Fact]
    public async Task GetByNomeAsync_QueryPlan_ShouldBeAbleToUseNomeUpperIndex()
    {
        var titulo = Titulo.Create(TipoTitulo.TesouroSelic, DataVencimento.Create(new DateOnly(2029, 3, 1)).Value).Value;
        await _writeRepository.AddAsync(titulo, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        await using var connection = await _dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition("SET enable_seqscan = off;", cancellationToken: CancellationToken.None));

        var planLines = (await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                EXPLAIN
                SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais,
                       CASE WHEN data_vencimento < @Today THEN true ELSE false END AS vencido
                FROM titulos
                WHERE UPPER(tipo_titulo || ' ' || EXTRACT(YEAR FROM data_vencimento)::text) = UPPER(@Nome)
                """,
                new { Nome = "Tesouro Selic 2029".Trim(), Today = DateOnly.FromDateTime(DateTime.UtcNow) },
                cancellationToken: CancellationToken.None))).ToList();

        var plan = string.Join(Environment.NewLine, planLines);
        plan.Should().Contain("ix_titulos_nome_upper");
    }
}
