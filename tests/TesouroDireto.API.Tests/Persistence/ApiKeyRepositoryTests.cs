using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.Persistence.Repositories;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class ApiKeyRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private NpgsqlDataSource _dataSource = null!;
    private ApiKeyWriteRepository _writeRepository = null!;
    private ApiKeyReadRepository _readRepository = null!;
    private Usuario _dono = null!;

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
        _writeRepository = new ApiKeyWriteRepository(_dbContext);
        _readRepository = new ApiKeyReadRepository(_dataSource);

        _dono = Usuario.Create(
            Email.Create("dono@exemplo.com").Value,
            "Dono das Keys",
            PapelUsuario.User,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

        await _dbContext.Usuarios.AddAsync(_dono, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ApiKey NovaApiKey(string rawKey, string nome = "Sistema Parceiro") =>
        ApiKey.Create(
            nome,
            ApiKeyHash.FromRawKey(rawKey),
            rawKey[..Math.Min(8, rawKey.Length)],
            _dono.Id,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

    [Fact]
    public async Task AddAsync_ShouldPersistApiKeyWithHashNotRawKey()
    {
        var apiKey = NovaApiKey("chave-secreta-1");

        var result = await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKey.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Hash.Value.Should().Be(ApiKeyHash.FromRawKey("chave-secreta-1").Value);
        found.Hash.Value.Should().NotBe("chave-secreta-1");
        found.Ativa.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_DuplicateHash_ShouldReturnAlreadyExists()
    {
        var apiKey1 = NovaApiKey("chave-duplicada");
        var apiKey2 = ApiKey.Create(
            "Outro Sistema",
            ApiKeyHash.FromRawKey("chave-duplicada"),
            "dup12345",
            _dono.Id,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

        await _writeRepository.AddAsync(apiKey1, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _writeRepository.AddAsync(apiKey2, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.AlreadyExists");
    }

    [Fact]
    public async Task AddAsync_WithNonExistentDono_ShouldThrowOnSave()
    {
        var apiKey = ApiKey.Create(
            "Sistema Orfao",
            ApiKeyHash.FromRawKey("chave-orfa"),
            "orf12345",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero)).Value;

        await _writeRepository.AddAsync(apiKey, CancellationToken.None);

        var act = () => _dbContext.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_AfterRevogar_ShouldPersistRevogadaEm()
    {
        var apiKey = NovaApiKey("chave-revogar");
        await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var quando = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);
        apiKey.Revogar(quando);

        var updateResult = await _writeRepository.UpdateAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        updateResult.IsSuccess.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKey.Id, CancellationToken.None);

        found!.Ativa.Should().BeFalse();
        found.RevogadaEm.Should().Be(quando);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ShouldReturnApiKey()
    {
        var apiKey = NovaApiKey("chave-por-id");
        await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        var result = await _writeRepository.GetByIdAsync(apiKey.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Nome.Should().Be("Sistema Parceiro");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldReturnNotFound()
    {
        var result = await _writeRepository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task GetByHashAsync_WhenExists_ShouldReturnDtoWithoutHashOrKey()
    {
        var apiKey = NovaApiKey("chave-leitura");
        await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetByHashAsync(ApiKeyHash.FromRawKey("chave-leitura").Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(apiKey.Id);
        result.Value.DonoUsuarioId.Should().Be(_dono.Id);
        result.Value.Ativa.Should().BeTrue();
    }

    [Fact]
    public async Task GetByHashAsync_WhenNotFound_ShouldReturnNotFound()
    {
        var result = await _readRepository.GetByHashAsync(ApiKeyHash.FromRawKey("chave-inexistente").Value, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task GetActiveByHashAsync_WhenActive_ShouldReturnDto()
    {
        var apiKey = NovaApiKey("chave-ativa");
        await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetActiveByHashAsync(ApiKeyHash.FromRawKey("chave-ativa").Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(apiKey.Id);
        result.Value.Ativa.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveByHashAsync_WhenRevoked_ShouldReturnNotFound()
    {
        var apiKey = NovaApiKey("chave-revogada-lookup");
        await _writeRepository.AddAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        apiKey.Revogar(new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero));
        await _writeRepository.UpdateAsync(apiKey, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.GetActiveByHashAsync(ApiKeyHash.FromRawKey("chave-revogada-lookup").Value, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task GetActiveByHashAsync_WhenNotFound_ShouldReturnNotFound()
    {
        var result = await _readRepository.GetActiveByHashAsync(ApiKeyHash.FromRawKey("chave-inexistente-ativa").Value, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task ListByDonoAsync_ShouldReturnAllKeysOfDono()
    {
        var apiKey1 = NovaApiKey("chave-lista-1");
        var apiKey2 = ApiKey.Create(
            "Segunda Key",
            ApiKeyHash.FromRawKey("chave-lista-2"),
            "list5678",
            _dono.Id,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero)).Value;

        await _writeRepository.AddAsync(apiKey1, CancellationToken.None);
        await _writeRepository.AddAsync(apiKey2, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _readRepository.ListByDonoAsync(_dono.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(k => k.Id).Should().Contain([apiKey1.Id, apiKey2.Id]);
    }

    [Fact]
    public async Task ListByDonoAsync_WithNoKeys_ShouldReturnEmpty()
    {
        var outroDono = Guid.NewGuid();

        var result = await _readRepository.ListByDonoAsync(outroDono, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
