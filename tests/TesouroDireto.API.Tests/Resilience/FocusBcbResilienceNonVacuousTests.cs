using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Resilience;

public sealed class FocusBcbResilienceNonVacuousTests
{
    private const string BaseUrl = "https://olinda.bcb.gov.br/olinda/servico/Expectativas/versao/v1/odata/";

    private const string SelicSuccessJson = """
        {
          "value": [{
            "Indicador": "Selic",
            "Data": "2026-01-01",
            "Media": 12.5,
            "Mediana": 12.5
          }]
        }
        """;

    [Fact]
    public async Task GetProjecaoAsync_WithRetryDisabled_WhenBcbFailsTwiceThenSucceeds_ShouldFail()
    {
        var handler = new SequenceHttpMessageHandler(
            failuresBeforeSuccess: 2,
            failureStatusCode: HttpStatusCode.ServiceUnavailable,
            successResponseFactory: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SelicSuccessJson, Encoding.UTF8, "application/json")
            });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FocusBcb:BaseUrl"] = BaseUrl,
                ["Resilience:FocusBcb:Retry:MaxAttempts"] = "1",
                ["Resilience:FocusBcb:Retry:BaseDelay"] = "00:00:00.010"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpClient<FocusBcbService>()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddFocusBcbResilienceHandler(configuration);

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<FocusBcbService>();

        var result = await service.GetProjecaoAsync(Indexador.Selic, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Projecao.HttpError");

        handler.Calls.Should().Be(2);
    }
}
