using System.Net;
using System.Net.Http.Headers;

namespace TesouroDireto.Web.Tests;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> that routes requests by HTTP method + relative
/// path (ignoring query string) to a caller-configured responder, so tests can control
/// exactly what the fake API returns without touching real network/infra.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _routes = [];

    public FakeHttpMessageHandler When(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes.Add((method, path, responder));
        return this;
    }

    public FakeHttpMessageHandler When(HttpMethod method, string path, HttpResponseMessage response)
        => When(method, path, _ => response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestPath = request.RequestUri?.AbsolutePath.Trim('/') ?? "";

        foreach (var (method, path, responder) in _routes)
        {
            if (method == request.Method && string.Equals(path.Trim('/'), requestPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(responder(request));
            }
        }

        throw new InvalidOperationException(
            $"No route configured for {request.Method} {requestPath}");
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    public static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string text)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return response;
    }

    public static HttpResponseMessage NoContentResponse(HttpStatusCode statusCode = HttpStatusCode.NoContent)
        => new(statusCode);
}
