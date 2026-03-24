using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Caching;

namespace TesouroDireto.Infrastructure.Tests.Caching;

public sealed class CacheInvalidationBehaviorTests : IDisposable
{
    private readonly MemoryCacheInvalidator _invalidator = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task ImportCsvCommand_Success_ShouldInvalidateTitulosAndPrecos()
    {
        var behavior = CreateBehavior<ImportCsvCommand, Result<ImportResult>>();
        SetCacheEntry("titulos:all:all", _invalidator.GetTitulosToken());
        SetCacheEntry("preco-atual:abc", _invalidator.GetPrecosToken());

        var result = new ImportResult(1, 10, 0, 0);
        await behavior.Handle(
            new ImportCsvCommand(),
            _ => Task.FromResult(Result<ImportResult>.Success(result)),
            CancellationToken.None);

        _cache.TryGetValue("titulos:all:all", out _).Should().BeFalse();
        _cache.TryGetValue("preco-atual:abc", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ImportCsvCommand_Failure_ShouldNotInvalidate()
    {
        var behavior = CreateBehavior<ImportCsvCommand, Result<ImportResult>>();
        SetCacheEntry("titulos:all:all", _invalidator.GetTitulosToken());

        await behavior.Handle(
            new ImportCsvCommand(),
            _ => Task.FromResult(Result<ImportResult>.Failure(new Error("Test", "fail"))),
            CancellationToken.None);

        _cache.TryGetValue("titulos:all:all", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ImportFeriadosCommand_Success_ShouldInvalidateFeriados()
    {
        var behavior = CreateBehavior<ImportFeriadosCommand, Result<ImportFeriadosResult>>();
        SetCacheEntry("feriados:datas", _invalidator.GetFeriadosToken());

        await behavior.Handle(
            new ImportFeriadosCommand(),
            _ => Task.FromResult(Result<ImportFeriadosResult>.Success(new ImportFeriadosResult(5, 0))),
            CancellationToken.None);

        _cache.TryGetValue("feriados:datas", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateTributoCommand_Success_ShouldInvalidateTributos()
    {
        var behavior = CreateBehavior<CreateTributoCommand, Result<Guid>>();
        SetCacheEntry("tributos:all", _invalidator.GetTributosToken());
        SetCacheEntry("tributos:ativos", _invalidator.GetTributosToken());

        var faixas = new[] { new FaixaDto(0, 180, null, 22.5m) };
        var command = new CreateTributoCommand("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false);

        await behavior.Handle(
            command,
            _ => Task.FromResult(Result<Guid>.Success(Guid.NewGuid())),
            CancellationToken.None);

        _cache.TryGetValue("tributos:all", out _).Should().BeFalse();
        _cache.TryGetValue("tributos:ativos", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTributoCommand_Success_ShouldInvalidateTributos()
    {
        var behavior = CreateBehavior<UpdateTributoCommand, Result>();
        SetCacheEntry("tributos:ativos", _invalidator.GetTributosToken());

        var faixas = new[] { new FaixaDto(0, 180, null, 22.5m) };
        var command = new UpdateTributoCommand(Guid.NewGuid(), true, faixas);

        await behavior.Handle(
            command,
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        _cache.TryGetValue("tributos:ativos", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownCommand_ShouldNotInvalidateAnything()
    {
        var behavior = CreateBehavior<UnrelatedCommand, Result>();
        SetCacheEntry("titulos:all:all", _invalidator.GetTitulosToken());
        SetCacheEntry("tributos:all", _invalidator.GetTributosToken());

        await behavior.Handle(
            new UnrelatedCommand(),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        _cache.TryGetValue("titulos:all:all", out _).Should().BeTrue();
        _cache.TryGetValue("tributos:all", out _).Should().BeTrue();
    }

    private CacheInvalidationBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>()
        where TRequest : notnull
    {
        return new CacheInvalidationBehavior<TRequest, TResponse>(_invalidator);
    }

    private void SetCacheEntry(string key, CancellationToken token)
    {
        var options = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(token));
        _cache.Set(key, "value", options);
    }

    public void Dispose() => _cache.Dispose();

    private sealed record UnrelatedCommand : IRequest<Result>;
}
