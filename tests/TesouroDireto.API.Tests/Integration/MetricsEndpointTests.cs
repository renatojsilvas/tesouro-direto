using System.Net;
using FluentAssertions;

namespace TesouroDireto.API.Tests.Integration;

/// <summary>
/// Prova, ponta a ponta (host real via Program.cs), que o MetricsBehavior (O6) está
/// plugado no pipeline do MediatR e que uma falha de negócio (Result.Failure, ex.:
/// Titulo.NotFound) vira outcome="failure" no Prometheus — nunca "exception".
/// </summary>
[Collection("api")]
public sealed class MetricsEndpointTests(ApiTestFactory factory) : IAsyncLifetime
{
    // /metrics é isento de ApiKey (ApiKey:ExcludedPaths) — cliente sem header.
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metrics_AfterSuccessAndBusinessFailureRequests_ShouldExposeMediatrOutcomes()
    {
        // Sucesso: GET /titulos passa pelo MediatR (GetTitulosQuery) e retorna 200.
        var successResponse = await factory.CreateAuthenticatedClient().GetAsync("/titulos", CancellationToken.None);
        successResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Falha de negócio (não exceção): nome de título inexistente -> Titulo.NotFound -> 404,
        // via GetPrecoAtualByNomeQuery/Result.Failure, sem exceção lançada.
        var failureResponse = await factory.CreateAuthenticatedClient()
            .GetAsync("/titulos/preco-atual?nome=Tesouro Inexistente 2099", CancellationToken.None);
        failureResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var metricsResponse = await _client.GetAsync("/metrics", CancellationToken.None);
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

        body.Should().Contain("mediatr_request_duration_seconds");
        body.Should().Contain("mediatr_requests_total");
        // Escopado por request_type: prova que ESTA query falha (GetPrecoAtualByNomeQuery)
        // virou outcome="failure" — não depende de poluição do registry global por outra
        // classe de teste (o registry do Prometheus é estático e compartilhado no processo).
        body.Should().Contain("request_type=\"GetPrecoAtualByNomeQuery\",outcome=\"failure\"");
    }
}
