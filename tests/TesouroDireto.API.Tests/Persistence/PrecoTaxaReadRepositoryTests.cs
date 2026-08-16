using Dapper;
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
                Taxa.Create(13.12m).Value, Taxa.Create(13.18m).Value,
                PrecoUnitario.Create(756.43m).Value, PrecoUnitario.Create(755.39m).Value,
                PrecoUnitario.Create(756.43m).Value).Value,
            PrecoTaxa.Create(_titulo.Id, DataBase.Create(new DateOnly(2024, 6, 15)).Value,
                Taxa.Create(10.50m).Value, Taxa.Create(10.75m).Value,
                PrecoUnitario.Create(800.00m).Value, PrecoUnitario.Create(799.00m).Value,
                PrecoUnitario.Create(798.00m).Value).Value,
            PrecoTaxa.Create(_titulo.Id, DataBase.Create(new DateOnly(2024, 12, 20)).Value,
                Taxa.Create(11.00m).Value, Taxa.Create(11.25m).Value,
                PrecoUnitario.Create(850.00m).Value, PrecoUnitario.Create(849.00m).Value,
                PrecoUnitario.Create(848.00m).Value).Value,
        };

        await _dbContext.PrecosTaxas.AddRangeAsync(precos, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
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

    [Fact]
    public async Task GetLatestByTituloIdAsync_ShouldReturnMostRecentPreco()
    {
        await SeedPrecos();

        var result = await _readRepository.GetLatestByTituloIdAsync(_titulo.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DataBase.Should().Be("2024-12-20");
        result.Value.TaxaCompra.Should().Be(11.00m);
    }

    [Fact]
    public async Task GetLatestByTituloIdAsync_WithNoPrecos_ShouldReturnNotFound()
    {
        var result = await _readRepository.GetLatestByTituloIdAsync(_titulo.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PrecoTaxa.NotFound");
    }

    [Fact]
    public async Task GetByDataBaseAsync_WithMultipleTitulosOnDate_ShouldReturnOneRowPerTituloOrderedByCodigoAscending()
    {
        var dataBase = new DateOnly(2025, 6, 10);

        var tituloSelic = Titulo.Create(
            TipoTitulo.TesouroSelic,
            DataVencimento.Create(new DateOnly(2030, 3, 1)).Value).Value;
        var tituloEduca = Titulo.Create(
            TipoTitulo.TesouroEduca,
            DataVencimento.Create(new DateOnly(2033, 12, 15)).Value).Value;
        var tituloPrefixado = Titulo.Create(
            TipoTitulo.TesouroPrefixado,
            DataVencimento.Create(new DateOnly(2028, 1, 1)).Value).Value;

        // A ordem de inserção (Selic, Educa+, Prefixado) é deliberadamente diferente da ordem
        // alfabética de codigo (educa, prefixado, selic), e todas as linhas compartilham a MESMA
        // data_base. Como o SQL não ordena (de propósito), remover o .OrderBy(dto.Codigo) da
        // projeção em PrecoTaxaReadRepository faz a resposta cair na ordem física/de inserção e
        // este assert fica vermelho — não-vacuidade provada por mutação.
        await _dbContext.Titulos.AddAsync(tituloSelic, CancellationToken.None);
        await _dbContext.Titulos.AddAsync(tituloEduca, CancellationToken.None);
        await _dbContext.Titulos.AddAsync(tituloPrefixado, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var precoSelic = PrecoTaxa.Create(
            tituloSelic.Id, DataBase.Create(dataBase).Value,
            Taxa.Create(13.00m).Value, Taxa.Create(13.10m).Value,
            PrecoUnitario.Create(100.00m).Value, PrecoUnitario.Create(99.00m).Value, PrecoUnitario.Create(100.00m).Value).Value;
        var precoEduca = PrecoTaxa.Create(
            tituloEduca.Id, DataBase.Create(dataBase).Value,
            Taxa.Create(9.00m).Value, Taxa.Create(9.20m).Value,
            PrecoUnitario.Create(500.00m).Value, PrecoUnitario.Create(499.00m).Value, PrecoUnitario.Create(500.00m).Value).Value;
        var precoPrefixado = PrecoTaxa.Create(
            tituloPrefixado.Id, DataBase.Create(dataBase).Value,
            Taxa.Create(11.00m).Value, Taxa.Create(11.20m).Value,
            PrecoUnitario.Create(900.00m).Value, PrecoUnitario.Create(899.00m).Value, PrecoUnitario.Create(900.00m).Value).Value;

        await _dbContext.PrecosTaxas.AddRangeAsync(new[] { precoSelic, precoEduca, precoPrefixado }, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByDataBaseAsync(dataBase, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        var codigoEsperadoEduca = TituloCodigo.From(TipoTitulo.TesouroEduca.Name, tituloEduca.DataVencimento.Value);
        var codigoEsperadoPrefixado = TituloCodigo.From(TipoTitulo.TesouroPrefixado.Name, tituloPrefixado.DataVencimento.Value);
        var codigoEsperadoSelic = TituloCodigo.From(TipoTitulo.TesouroSelic.Name, tituloSelic.DataVencimento.Value);

        result.Value.Select(p => p.Codigo).Should().Equal(codigoEsperadoEduca, codigoEsperadoPrefixado, codigoEsperadoSelic);
    }

    [Fact]
    public async Task GetByDataBaseAsync_WithNoPrecosOnDate_ShouldReturnSuccessWithEmptyList()
    {
        await SeedPrecos();

        var result = await _readRepository.GetByDataBaseAsync(new DateOnly(2024, 3, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDataBaseAsync_WithNullFields_ShouldReturnNullDecimalValues()
    {
        var dataBase = new DateOnly(2025, 2, 14);
        var preco = PrecoTaxa.Create(
            _titulo.Id, DataBase.Create(dataBase).Value,
            null, null, null, null, null).Value;

        await _dbContext.PrecosTaxas.AddAsync(preco, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByDataBaseAsync(dataBase, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var dto = result.Value.Single();
        dto.TaxaCompra.Should().BeNull();
        dto.TaxaVenda.Should().BeNull();
        dto.PuCompra.Should().BeNull();
        dto.PuVenda.Should().BeNull();
        dto.PuBase.Should().BeNull();
    }

    [Fact]
    public async Task GetByDataBaseAsync_QueryPlan_ShouldBeAbleToUseDataBaseIndex()
    {
        await SeedPrecos();

        await using var connection = await _dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition("SET enable_seqscan = off;", cancellationToken: CancellationToken.None));

        var planLines = (await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                EXPLAIN
                SELECT t.tipo_titulo, t.data_vencimento, p.data_base, p.taxa_compra, p.taxa_venda,
                       p.pu_compra, p.pu_venda, p.pu_base
                FROM precos_taxas p
                JOIN titulos t ON t.id = p.titulo_id
                WHERE p.data_base = @DataBase
                ORDER BY t.tipo_titulo, t.data_vencimento
                """,
                new { DataBase = new DateOnly(2024, 6, 15) },
                cancellationToken: CancellationToken.None))).ToList();

        var plan = string.Join(Environment.NewLine, planLines);
        plan.Should().Contain("ix_precos_taxas_data_base");
    }
}
