using System.Net;

namespace TesouroDireto.API.Tests.Resilience;

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
