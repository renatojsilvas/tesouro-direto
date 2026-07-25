using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using TesouroDireto.API.Extensions;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.API.Tests.Extensions;

public sealed class DatabaseInitializerTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IFeriadoReadRepository _feriadoReadRepository = Substitute.For<IFeriadoReadRepository>();
    private readonly IDatabaseMigrator _migrator = Substitute.For<IDatabaseMigrator>();
    private readonly ILogger<DatabaseInitializer> _logger = Substitute.For<ILogger<DatabaseInitializer>>();

    private DatabaseInitializer BuildSut(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_sender);
        services.AddSingleton(_feriadoReadRepository);
        services.AddSingleton(_migrator);
        var provider = services.BuildServiceProvider();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return new DatabaseInitializer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            environment,
            _logger);
    }

    [Fact]
    public async Task InitializeAsync_HappyPath_MigratesSeedsAndImportsFeriados()
    {
        var sut = BuildSut("Production");

        _sender.Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _feriadoReadRepository.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));
        _sender.Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportFeriadosResult>.Success(new ImportFeriadosResult(0, 0)));

        var act = () => sut.InitializeAsync();

        await act.Should().NotThrowAsync();
        await _migrator.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_WhenSeedTributosFails_ThrowsAndSkipsFeriadosImport()
    {
        var sut = BuildSut("Production");

        _sender.Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("Tributos.Error", "Seed quebrado")));
        _feriadoReadRepository.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));
        _sender.Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportFeriadosResult>.Success(new ImportFeriadosResult(0, 0)));

        var act = () => sut.InitializeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _sender.DidNotReceive().Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_WhenFeriadosImportFails_DoesNotThrowAndLogsWarning()
    {
        var sut = BuildSut("Production");

        _sender.Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _feriadoReadRepository.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(Array.Empty<DateOnly>()));
        _sender.Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportFeriadosResult>.Failure(new Error("Feriados.Error", "Import quebrado")));

        var act = () => sut.InitializeAsync();

        await act.Should().NotThrowAsync();
        await _sender.Received(1).Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());

        var warningCalls = _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Where(c => (LogLevel)c.GetArguments()[0]! == LogLevel.Warning)
            .ToList();

        warningCalls.Should().ContainSingle(c =>
            c.GetArguments()[2]!.ToString()!.Contains("Import de feriados no primeiro boot falhou"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFeriadosCheckFails_DoesNotThrowLogsWarningAndSkipsImport()
    {
        var sut = BuildSut("Production");

        _sender.Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _feriadoReadRepository.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Failure(new Error("Feriados.Error", "Checagem quebrada")));

        var act = () => sut.InitializeAsync();

        await act.Should().NotThrowAsync();
        await _sender.DidNotReceive().Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());

        var warningCalls = _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Where(c => (LogLevel)c.GetArguments()[0]! == LogLevel.Warning)
            .ToList();

        warningCalls.Should().ContainSingle(c =>
            c.GetArguments()[2]!.ToString()!.Contains("Checagem de feriados no boot falhou"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFeriadosAlreadySeeded_SkipsImportSilently()
    {
        var sut = BuildSut("Production");

        _sender.Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _feriadoReadRepository.GetAllDatasAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<DateOnly>>.Success(new[] { new DateOnly(2026, 1, 1) }));

        var act = () => sut.InitializeAsync();

        await act.Should().NotThrowAsync();
        await _sender.Received(1).Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_WhenTesting_SkipsMigrationAndSeed()
    {
        var sut = BuildSut("Testing");

        await sut.InitializeAsync();

        await _migrator.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<SeedTributosCommand>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>());
    }
}
