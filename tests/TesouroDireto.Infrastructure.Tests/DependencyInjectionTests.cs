using System.Data.Common;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x;Username=x;Password=x"
            })
            .Build();

    [Fact]
    public void AddInfrastructure_NpgsqlDataSource_HasNoResetOnCloseEnabled()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        var builder = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        builder.NoResetOnClose.Should().BeTrue();
    }

    [Fact]
    public void AddInfrastructure_NpgsqlDataSource_HasMaxPoolSizeCapped()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        var builder = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        builder.MaxPoolSize.Should().Be(10);
    }

    [Fact]
    public void AddInfrastructure_NpgsqlDataSource_IsRegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<NpgsqlDataSource>();
        using var scope = provider.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void AddInfrastructure_AppDbContext_ReusesTheSameNpgsqlDataSourceSingleton_NotASecondPool()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var registeredDataSource = provider.GetRequiredService<NpgsqlDataSource>();

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var options = ((IInfrastructure<IServiceProvider>)dbContext).Instance
            .GetRequiredService<IDbContextOptions>();

        var npgsqlExtension = options.Extensions
            .Single(e => e.GetType().Name == "NpgsqlOptionsExtension");

        var dataSourceProperty = npgsqlExtension.GetType()
            .GetProperty("DataSource", BindingFlags.Public | BindingFlags.Instance)!;

        var dbContextDataSource = (DbDataSource?)dataSourceProperty.GetValue(npgsqlExtension);

        dbContextDataSource.Should().BeSameAs(registeredDataSource,
            "o EF Core deve usar o MESMO NpgsqlDataSource singleton dos read repositories via Dapper — " +
            "duas instâncias distintas voltariam a abrir duas pools da mesma connection string");
    }

    [Fact]
    public void AddInfrastructure_MemoryCache_HasSizeLimitConfigured_EntryWithoutSizeThrows()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        var act = () => cache.Set("probe-sem-size", "valor", new MemoryCacheEntryOptions());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Size*");
    }
}
