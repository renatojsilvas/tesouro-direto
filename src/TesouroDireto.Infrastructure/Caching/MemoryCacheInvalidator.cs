namespace TesouroDireto.Infrastructure.Caching;

public sealed class MemoryCacheInvalidator
{
    private CancellationTokenSource _titulosCts = new();
    private CancellationTokenSource _precosCts = new();
    private CancellationTokenSource _tributosCts = new();
    private CancellationTokenSource _feriadosCts = new();

    public CancellationToken GetTitulosToken() => _titulosCts.Token;
    public CancellationToken GetPrecosToken() => _precosCts.Token;
    public CancellationToken GetTributosToken() => _tributosCts.Token;
    public CancellationToken GetFeriadosToken() => _feriadosCts.Token;

    public void InvalidateTitulos() => Invalidate(ref _titulosCts);
    public void InvalidatePrecos() => Invalidate(ref _precosCts);
    public void InvalidateTributos() => Invalidate(ref _tributosCts);
    public void InvalidateFeriados() => Invalidate(ref _feriadosCts);

    private static void Invalidate(ref CancellationTokenSource cts)
    {
        var old = Interlocked.Exchange(ref cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
