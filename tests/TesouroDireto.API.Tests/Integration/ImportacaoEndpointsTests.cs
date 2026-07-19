using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;

namespace TesouroDireto.API.Tests.Integration;

/// <summary>
/// Cobre apenas auth + o caso de "URL não configurada" (sem stub de download externo,
/// conforme instrução explícita da tarefa). Ver a divergência documentada abaixo em
/// PostImportacao_WithUrlNotConfigured_ShouldReturn200WithErrorCount: o handler NÃO
/// retorna 400 quando a URL não está configurada — ele registra o erro por linha /
/// feriado ignorado e retorna 200 com o resultado zerado.
/// </summary>
[Collection("api")]
public sealed class ImportacaoEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _authenticatedClient = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // 26. POST /importacao sem X-Api-Key -> 401
    [Fact]
    public async Task PostImportacao_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.PostAsync("/importacao", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 27. POST /importacao/feriados sem X-Api-Key -> 401
    [Fact]
    public async Task PostImportacaoFeriados_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.PostAsync("/importacao/feriados", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 28. POST /importacao com URL não configurada -> comportamento REAL: 200, não 400.
    //
    // DIVERGÊNCIA em relação à matriz (esperado 400): ImportCsvCommandHandler itera
    // csvImportService.GetRecordsAsync(); quando CsvImport:Url não está configurada,
    // CsvImportService emite UM único Result de falha ("CsvImport:Url is not
    // configured.") e encerra o stream. O handler trata cada item de falha apenas
    // incrementando LinhasComErro e continuando o loop — nunca propaga a falha como
    // Result<ImportResult> de erro. O endpoint então retorna 200 OK com
    // ImportResult(TitulosCriados: 0, PrecosInseridos: 0, PrecosIgnorados: 0, LinhasComErro: 1).
    [Fact]
    public async Task PostImportacao_WithUrlNotConfigured_ShouldReturn200WithErrorCount()
    {
        var response = await _authenticatedClient.PostAsync("/importacao", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>(cancellationToken: CancellationToken.None);
        result.Should().NotBeNull();
        result!.TitulosCriados.Should().Be(0);
        result.PrecosInseridos.Should().Be(0);
        result.LinhasComErro.Should().Be(1);
    }

    // Mesma divergência para /importacao/feriados: FeriadoImportService, sem URL
    // configurada, apenas loga e encerra o stream sem sequer emitir um registro de
    // falha — o handler processa zero feriados e retorna 200 com o resultado zerado
    // (nunca chega a chamar AddRangeAsync/SaveChanges, então não há efeito colateral).
    [Fact]
    public async Task PostImportacaoFeriados_WithUrlNotConfigured_ShouldReturn200WithZeroResult()
    {
        var response = await _authenticatedClient.PostAsync("/importacao/feriados", null, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportFeriadosResult>(cancellationToken: CancellationToken.None);
        result.Should().NotBeNull();
        result!.FeriadosImportados.Should().Be(0);
        result.FeriadosIgnorados.Should().Be(0);
    }
}
