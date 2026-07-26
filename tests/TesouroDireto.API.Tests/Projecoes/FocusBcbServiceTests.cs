using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Projecoes;

public sealed class FocusBcbServiceTests
{
    private const string BaseUrl = "https://olinda.bcb.gov.br/olinda/servico/Expectativas/versao/v1/odata/";

    [Fact]
    public async Task GetProjecaoAsync_Selic_ShouldParseCorrectly()
    {
        var json = """
            {
              "value": [{
                "Indicador": "Selic",
                "Data": "2026-03-13",
                "Reuniao": "R4/2026",
                "Media": 14.75,
                "Mediana": 14.25,
                "DesvioPadrao": 0.5,
                "Minimo": 13.5,
                "Maximo": 15.0,
                "numeroRespondentes": 120,
                "baseCalculo": 0
              }]
            }
            """;
        var service = CreateService(json);

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Indicador.Should().Be("Selic");
        result.Value.MedianaAnual.Should().Be(14.25m);
        result.Value.MediaAnual.Should().Be(14.75m);
        result.Value.DataReferencia.Should().Be(new DateOnly(2026, 3, 13));
    }

    [Fact]
    public async Task GetProjecaoAsync_IPCA_ShouldParseCorrectly()
    {
        var json = """
            {
              "value": [{
                "Indicador": "IPCA",
                "Data": "2026-03-13",
                "Suavizada": "N",
                "Media": 3.97,
                "Mediana": 3.98,
                "DesvioPadrao": 0.3,
                "Minimo": 3.04,
                "Maximo": 5.24,
                "numeroRespondentes": 98,
                "baseCalculo": 1
              }]
            }
            """;
        var service = CreateService(json);

        var result = await service.GetProjecaoAsync(Indexador.IPCA, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Indicador.Should().Be("IPCA");
        result.Value.MedianaAnual.Should().Be(3.98m);
    }

    [Fact]
    public async Task GetProjecaoAsync_IGPM_ShouldParseCorrectly()
    {
        var json = """
            {
              "value": [{
                "Indicador": "IGP-M",
                "Data": "2026-03-13",
                "Suavizada": "N",
                "Media": 4.42,
                "Mediana": 4.61,
                "DesvioPadrao": 0.85,
                "Minimo": 2.85,
                "Maximo": 7.55,
                "numeroRespondentes": 41,
                "baseCalculo": 1
              }]
            }
            """;
        var service = CreateService(json);

        var result = await service.GetProjecaoAsync(Indexador.IGPM, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Indicador.Should().Be("IGP-M");
        result.Value.MedianaAnual.Should().Be(4.61m);
    }

    [Fact]
    public async Task GetProjecaoAsync_Prefixado_ShouldReturnFailure()
    {
        var service = CreateService("{}");

        var result = await service.GetProjecaoAsync(Indexador.Prefixado, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.IndexadorNaoSuportado");
    }

    [Fact]
    public async Task GetProjecaoAsync_EmptyResponse_ShouldReturnFailure()
    {
        var json = """{"value": []}""";
        var service = CreateService(json);

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.NotFound");
    }

    [Fact]
    public async Task GetProjecaoAsync_HttpError_ShouldReturnFailure()
    {
        var service = CreateService("", HttpStatusCode.InternalServerError);

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.HttpError");
    }

    [Fact]
    public async Task GetProjecaoAsync_HttpException_ShouldReturnFailure()
    {
        var handler = new ExceptionHttpMessageHandler();
        var service = CreateServiceWithHandler(handler);

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.HttpError");
    }

    [Fact]
    public async Task GetProjecaoAsync_MissingUrl_ShouldReturnFailure()
    {
        var service = CreateService("{}", url: "");

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.UrlNotConfigured");
    }

    [Fact]
    public async Task GetProjecaoAsync_HttpUrl_ShouldReturnFailure()
    {
        var service = CreateService("{}", url: "http://insecure.example.com/");

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.UrlNotConfigured");
    }

    [Fact]
    public async Task GetProjecaoAsync_WhenCallerCancels_ShouldPropagateOperationCanceled_NotHttpError()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateServiceWithHandler(new CallerCancellingHandler(cts));

        var act = async () => await service.GetProjecaoAsync(Indexador.Selic, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetProjecaoAsync_WhenInternalTimeoutCancels_ShouldReturnHttpError_NotPropagate()
    {
        var service = CreateServiceWithHandler(new InternalTimeoutHandler());

        // Chamador NUNCA cancela (CancellationToken.None).
        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.HttpError");
    }

    private static FocusBcbService CreateService(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string url = BaseUrl)
    {
        var handler = new FakeHttpMessageHandler(responseJson, statusCode);
        return CreateServiceWithHandler(handler, url);
    }

    private static FocusBcbService CreateServiceWithHandler(
        HttpMessageHandler handler, string url = BaseUrl)
    {
        var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FocusBcb:BaseUrl"] = url
            })
            .Build();
        var logger = Substitute.For<ILogger<FocusBcbService>>();

        return new FocusBcbService(httpClient, configuration, logger);
    }

    private sealed class FakeHttpMessageHandler(
        string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ExceptionHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    // Cancela o token do CHAMADOR e então observa o cancelamento — como um cancelamento
    // legítimo pedido por quem chamou (não uma falha do BCB).
    private sealed class CallerCancellingHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await cts.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // Simula timeout INTERNO (HttpClient.Timeout / Polly AttemptTimeout): TaskCanceledException
    // com um token JÁ cancelado que NÃO é o do chamador. Como o token do chamador não está
    // cancelado, o filtro `when` do FocusBcbService falha e cai no catch amplo -> HttpError.
    private sealed class InternalTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new TaskCanceledException("internal timeout", null, new CancellationToken(canceled: true));
    }
}
