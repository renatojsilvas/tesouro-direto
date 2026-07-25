using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Resilience;

/// <summary>
/// Tarefa 13 (item d) — não-vacuidade: mesmo cenário de
/// <see cref="FocusBcbResilienceEndpointTests.PostSimulador_WhenBcbFailsTwiceThenSucceeds_ShouldRetryAndReturn200"/>
/// (handler falha 2x com 503 e sucede na 3ª), mas com <c>Resilience:FocusBcb:Retry:MaxAttempts=1</c>
/// (o mínimo aceito pela biblioteca — <c>Polly</c> valida <c>MaxRetryAttempts &gt;= 1</c>, "0
/// tentativas" não é uma configuração possível). Com só 1 retry (2 tentativas no total),
/// as 2 tentativas batem nas 2 falhas propositais do handler e NUNCA alcançam a 3ª
/// resposta (a que sucede) — prova que é o número de retries, e não outra coisa (ex.:
/// cache, alguma tolerância a erro no parser), o responsável pelo sucesso do outro teste,
/// que usa o default de produção (2 retries).
/// </summary>
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

        // Com só 1 retry, foram feitas 2 tentativas (a inicial + 1 retry) — nenhuma delas
        // chega na 3ª chamada, a que sucederia.
        handler.Calls.Should().Be(2);
    }
}
