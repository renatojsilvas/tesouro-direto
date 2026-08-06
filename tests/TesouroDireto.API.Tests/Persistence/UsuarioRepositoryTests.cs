using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Persistence;

public sealed class UsuarioRepositoryTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private AppDbContext _dbContext = null!;
    private IUsuarioWriteRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync(CancellationToken.None);

        _repository = new TesouroDireto.Infrastructure.Persistence.Repositories.UsuarioWriteRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static Usuario NovoUsuario(string email, string? googleSub = null) =>
        Usuario.Create(
            Email.Create(email).Value,
            "Fulano de Tal",
            PapelUsuario.User,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
            googleSub).Value;

    [Fact]
    public async Task AddAsync_ShouldPersistUsuario()
    {
        var usuario = NovoUsuario("teste@exemplo.com");

        var result = await _repository.AddAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == usuario.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Email.Value.Should().Be("teste@exemplo.com");
        found.Nome.Should().Be("Fulano de Tal");
        found.Papel.Should().Be(PapelUsuario.User);
        found.Aprovado.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_DuplicateEmail_ShouldReturnAlreadyExists()
    {
        var usuario1 = NovoUsuario("duplicado@exemplo.com");
        var usuario2 = NovoUsuario("duplicado@exemplo.com");

        await _repository.AddAsync(usuario1, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _repository.AddAsync(usuario2, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.AlreadyExists");
    }

    [Fact]
    public async Task GetByEmailAsync_WhenExists_ShouldReturnUsuario()
    {
        var usuario = NovoUsuario("busca-email@exemplo.com");
        await _repository.AddAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByEmailAsync(Email.Create("busca-email@exemplo.com").Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(usuario.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenNotFound_ShouldReturnNotFound()
    {
        var result = await _repository.GetByEmailAsync(Email.Create("inexistente@exemplo.com").Value, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.NotFound");
    }

    [Fact]
    public async Task GetByGoogleSubAsync_WhenExists_ShouldReturnUsuario()
    {
        var usuario = NovoUsuario("com-sub@exemplo.com", "google-sub-abc");
        await _repository.AddAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByGoogleSubAsync("google-sub-abc", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(usuario.Id);
    }

    [Fact]
    public async Task GetByGoogleSubAsync_WhenNotFound_ShouldReturnNotFound()
    {
        var result = await _repository.GetByGoogleSubAsync("sub-inexistente", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.NotFound");
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ShouldReturnUsuario()
    {
        var usuario = NovoUsuario("busca-id@exemplo.com");
        await _repository.AddAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        var result = await _repository.GetByIdAsync(usuario.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Value.Should().Be("busca-id@exemplo.com");
    }

    [Fact]
    public async Task UpdateAsync_AfterAprovar_ShouldPersistApprovalFields()
    {
        var usuario = NovoUsuario("aprovar@exemplo.com");
        await _repository.AddAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var adminId = Guid.NewGuid();
        var quando = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
        usuario.Aprovar(adminId, quando);

        var updateResult = await _repository.UpdateAsync(usuario, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        updateResult.IsSuccess.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();

        var found = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == usuario.Id, CancellationToken.None);

        found!.Aprovado.Should().BeTrue();
        found.AprovadoEm.Should().Be(quando);
        found.AprovadoPor.Should().Be(adminId);
    }

    [Fact]
    public async Task MultipleUsuarios_WithNullGoogleSub_ShouldNotViolateUniqueIndex()
    {
        var usuario1 = NovoUsuario("sem-sub-1@exemplo.com");
        var usuario2 = NovoUsuario("sem-sub-2@exemplo.com");

        await _repository.AddAsync(usuario1, CancellationToken.None);
        await _repository.AddAsync(usuario2, CancellationToken.None);

        var act = () => _dbContext.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Aprovar_ShouldAllowSelfReferencingAprovadoPor()
    {
        var admin = NovoUsuario("admin-self@exemplo.com");
        await _repository.AddAsync(admin, CancellationToken.None);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var quando = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
        admin.Aprovar(admin.Id, quando);

        var updateResult = await _repository.UpdateAsync(admin, CancellationToken.None);

        var act = () => _dbContext.SaveChangesAsync(CancellationToken.None);

        updateResult.IsSuccess.Should().BeTrue();
        await act.Should().NotThrowAsync();
    }
}
