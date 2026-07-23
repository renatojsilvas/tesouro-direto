using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Persistence;
using Xunit;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class SeedTributosIntegrationTests(ApiTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seed_OnEmptyDatabase_PersistsIofAndIrWithFaixas()
    {
        await factory.SeedAsync(async sp =>
        {
            var sender = sp.GetRequiredService<ISender>();
            (await sender.Send(new SeedTributosCommand())).IsSuccess.Should().BeTrue();
        });

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var tributos = await db.Set<Tributo>().ToListAsync();

            tributos.Should().HaveCount(2);
            var iof = tributos.Single(t => t.Nome == "IOF");
            iof.Faixas.Should().HaveCount(29);
            iof.Cumulativo.Should().BeTrue();
            var ir = tributos.Single(t => t.Nome == "Imposto de Renda");
            ir.Faixas.Should().HaveCount(4);
            ir.Faixas.Should().ContainSingle(f => f.DiasMin == 0 && f.DiasMax == 180 && f.Aliquota == 22.5m);
        });
    }

    [Fact]
    public async Task Seed_RunTwice_IsIdempotent()
    {
        await factory.SeedAsync(async sp =>
        {
            var sender = sp.GetRequiredService<ISender>();
            await sender.Send(new SeedTributosCommand());
            await sender.Send(new SeedTributosCommand());
        });

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Set<Tributo>().CountAsync()).Should().Be(2);
        });
    }
}
