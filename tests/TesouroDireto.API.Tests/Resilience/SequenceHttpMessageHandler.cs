using System.Net;

namespace TesouroDireto.API.Tests.Resilience;

/// <summary>
/// Handler HTTP fake compartilhado pelos testes de resiliência (tarefa 13): devolve
/// <paramref name="failureStatusCode"/> nas primeiras <paramref name="failuresBeforeSuccess"/>
/// chamadas e a resposta de <paramref name="successResponseFactory"/> a partir daí em
/// diante. <see cref="Calls"/> conta TODAS as chamadas (thread-safe), permitindo provar
/// quantas tentativas o pipeline de resiliência de fato fez.
/// </summary>
internal sealed class SequenceHttpMessageHandler(
    int failuresBeforeSuccess,
    HttpStatusCode failureStatusCode,
    Func<HttpResponseMessage> successResponseFactory) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _calls);

        var response = attempt <= failuresBeforeSuccess
            ? new HttpResponseMessage(failureStatusCode)
            : successResponseFactory();

        return Task.FromResult(response);
    }
}
