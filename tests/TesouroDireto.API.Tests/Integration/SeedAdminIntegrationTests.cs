using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Usuarios;
using TesouroDireto.Infrastructure.Persistence;
using Xunit;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SeedAdminIntegrationTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seed_RunTwice_CreatesSingleApprovedAdminAndIsIdempotent()
    {
        await factory.SeedAsync(async sp =>
        {
            var repo = sp.GetRequiredService<IUsuarioWriteRepository>();
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ADMIN_EMAIL"] = "admin@tesouro.test" })
                .Build();
            var handler = new SeedAdminCommandHandler(
                repo, uow, config, TimeProvider.System, NullLogger<SeedAdminCommandHandler>.Instance);

            (await handler.Handle(new SeedAdminCommand(), CancellationToken.None)).IsSuccess.Should().BeTrue();
            (await handler.Handle(new SeedAdminCommand(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        });

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var email = Email.Create("admin@tesouro.test").Value;
            var admins = await db.Usuarios.Where(u => u.Email == email).ToListAsync();

            admins.Should().ContainSingle();
            admins[0].Papel.Should().Be(PapelUsuario.Admin);
            admins[0].Aprovado.Should().BeTrue();
            admins[0].GoogleSub.Should().BeNull();
        });
    }
}
