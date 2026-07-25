using System.Net;

namespace TesouroDireto.API.Tests.Resilience;

/// <summary>
/// Handler HTTP fake que sempre demora mais que o <c>AttemptTimeout</c> configurado
/// (tarefa 13, correção pós-revisão) — usado para provar que
/// <c>Polly.Timeout.TimeoutRejectedException</c> é degradado graciosamente pelos adapters
/// de import (CSV/Feriados), em vez de escapar do <c>await foreach</c> como exceção não
/// tratada. Observa o <see cref="CancellationToken"/> recebido (o mesmo que o Polly
/// TimeoutStrategy cancela internamente quando o AttemptTimeout dispara) — o resultado
/// final (200 OK) nunca chega a ser observado pelo chamador, pois o Polly já terá
/// desistido de esperar bem antes.
/// </summary>
internal sealed class DelayHttpMessageHandler(TimeSpan delay) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        await Task.Delay(delay, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };
    }
}
