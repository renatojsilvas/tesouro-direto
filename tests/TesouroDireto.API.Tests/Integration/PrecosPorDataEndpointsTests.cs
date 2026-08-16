using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.PrecosTaxas;
using TesouroDireto.Domain.Titulos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class PrecosPorDataEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedTituloAsync(TipoTitulo tipo, DateOnly dataVencimento)
    {
        var tituloId = Guid.Empty;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var titulo = Titulo.Create(tipo, DataVencimento.Create(dataVencimento).Value).Value;
            await db.Titulos.AddAsync(titulo);
            await db.SaveChangesAsync();
            tituloId = titulo.Id;
        });

        return tituloId;
    }

    private async Task SeedPrecoAsync(
        Guid tituloId,
        DateOnly dataBase,
        decimal taxaCompra,
        decimal taxaVenda,
        decimal puCompra,
        decimal puVenda,
        decimal puBase)
    {
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var preco = PrecoTaxa.Create(
                tituloId,
                DataBase.Create(dataBase).Value,
                Taxa.Create(taxaCompra).Value,
                Taxa.Create(taxaVenda).Value,
                PrecoUnitario.Create(puCompra).Value,
                PrecoUnitario.Create(puVenda).Value,
                PrecoUnitario.Create(puBase).Value).Value;
            await db.PrecosTaxas.AddAsync(preco);
            await db.SaveChangesAsync();
        });
    }

    private static string Fmt(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static HttpRequestMessage ConditionalGet(string uri, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return request;
    }

    // A ordem de inserção (Selic, Educa+, Prefixado) é deliberadamente diferente da ordem
    // alfabética de codigo (educa, prefixado, selic), e todas as linhas compartilham a MESMA
    // data_base. Como o SQL não ordena (de propósito), remover o .OrderBy(dto.Codigo) da
    // projeção em PrecoTaxaReadRepository faz a resposta cair na ordem física/de inserção e
    // este assert fica vermelho — não-vacuidade provada por mutação.
    [Fact]
    public async Task GetPrecos_WithDataComPrecosDeVariosTitulos_ShouldReturnOnlyThatDateOrderedByCodigoAsc()
    {
        var dataBase = new DateOnly(2024, 1, 10);
        var outraData = dataBase.AddDays(1);

        var selicId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2029, 3, 1));
        var educaId = await SeedTituloAsync(TipoTitulo.TesouroEduca, new DateOnly(2035, 5, 15));
        var prefixadoId = await SeedTituloAsync(TipoTitulo.TesouroPrefixado, new DateOnly(2031, 1, 1));

        await SeedPrecoAsync(selicId, dataBase, 10.10m, 10.20m, 100.10m, 100.20m, 100.30m);
        await SeedPrecoAsync(selicId, outraData, 91m, 92m, 910m, 920m, 930m);

        await SeedPrecoAsync(educaId, dataBase, 11.10m, 11.20m, 200.10m, 200.20m, 200.30m);
        await SeedPrecoAsync(educaId, outraData, 81m, 82m, 810m, 820m, 830m);

        await SeedPrecoAsync(prefixadoId, dataBase, 12.10m, 12.20m, 300.10m, 300.20m, 300.30m);
        await SeedPrecoAsync(prefixadoId, outraData, 71m, 72m, 710m, 720m, 730m);

        var response = await _client.GetAsync($"/v1/precos?dataBase={Fmt(dataBase)}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDiaDto>>(JsonOptions, CancellationToken.None);
        precos.Should().NotBeNull();
        precos!.Should().HaveCount(3, "só as linhas de dataBase devem voltar, uma por título");

        var codigoEduca = TituloCodigo.From(TipoTitulo.TesouroEduca.Name, new DateOnly(2035, 5, 15));
        var codigoPrefixado = TituloCodigo.From(TipoTitulo.TesouroPrefixado.Name, new DateOnly(2031, 1, 1));
        var codigoSelic = TituloCodigo.From(TipoTitulo.TesouroSelic.Name, new DateOnly(2029, 3, 1));
        var codigosEsperadosNaOrdem = new[] { codigoEduca, codigoPrefixado, codigoSelic };

        precos.Should().BeInAscendingOrder(p => p.Codigo, StringComparer.Ordinal);
        precos.Select(p => p.Codigo).Should().Equal(codigosEsperadosNaOrdem);

        response.Headers.TryGetValues("X-Total-Count", out var totalValues).Should().BeTrue();
        totalValues!.Single().Should().Be("3");

        var educa = precos.Single(p => p.Codigo == codigoEduca);
        educa.DataBase.Should().Be(Fmt(dataBase));
        educa.TaxaCompra.Should().Be(11.10m);
        educa.TaxaVenda.Should().Be(11.20m);
        educa.PuCompra.Should().Be(200.10m);
        educa.PuVenda.Should().Be(200.20m);
        educa.PuBase.Should().Be(200.30m);

        var prefixado = precos.Single(p => p.Codigo == codigoPrefixado);
        prefixado.DataBase.Should().Be(Fmt(dataBase));
        prefixado.TaxaCompra.Should().Be(12.10m);
        prefixado.TaxaVenda.Should().Be(12.20m);
        prefixado.PuCompra.Should().Be(300.10m);
        prefixado.PuVenda.Should().Be(300.20m);
        prefixado.PuBase.Should().Be(300.30m);

        var selic = precos.Single(p => p.Codigo == codigoSelic);
        selic.DataBase.Should().Be(Fmt(dataBase));
        selic.TaxaCompra.Should().Be(10.10m);
        selic.TaxaVenda.Should().Be(10.20m);
        selic.PuCompra.Should().Be(100.10m);
        selic.PuVenda.Should().Be(100.20m);
        selic.PuBase.Should().Be(100.30m);
    }

    // Critério 2: dia válido sem nenhum preço semeado (domingo/feriado/importação ainda
    // não rodou) nunca é 404 — é 200 com corpo vazio.
    [Fact]
    public async Task GetPrecos_WithValidDateWithoutData_ShouldReturn200WithEmptyArrayAndZeroTotalCount_NeverNotFound()
    {
        var domingoSemPregao = new DateOnly(2024, 1, 7);

        var response = await _client.GetAsync($"/v1/precos?dataBase={Fmt(domingoSemPregao)}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "dia sem pregão é resultado legítimo, não recurso inexistente");

        var precos = await response.Content.ReadFromJsonAsync<List<PrecoTaxaDiaDto>>(JsonOptions, CancellationToken.None);
        precos.Should().NotBeNull();
        precos!.Should().BeEmpty();

        response.Headers.TryGetValues("X-Total-Count", out var totalValues).Should().BeTrue();
        totalValues!.Single().Should().Be("0");
    }

    // Critério 3a: dataBase ausente. Regressão específica: bindar como DateOnly?
    // deixa o binder aceitar "ausente" como null sem bloquear, então isto teria
    // de ser barrado pelo handler — se a validação sumir, aqui vira 200.
    [Fact]
    public async Task GetPrecos_WithoutDataBaseParam_ShouldReturn400WithDataBaseInvalidaCode()
    {
        var response = await _client.GetAsync("/v1/precos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("PrecoTaxa.DataBaseInvalida");
    }

    // Critério 3b: dataBase malformada. Regressão específica (achado 1 do PLANO):
    // bindar como DateOnly? faz o binder lançar BadHttpRequestException, que o
    // UseExceptionHandler reescreve como 500 sem "code" — o assert de NotBe(500)
    // é o que morre se a correção (bindar string? e parsear no handler) regredir.
    [Fact]
    public async Task GetPrecos_WithMalformedDataBaseParam_ShouldReturn400WithDataBaseInvalidaCode_NeverInternalServerError()
    {
        var response = await _client.GetAsync("/v1/precos?dataBase=31-12-2025", CancellationToken.None);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("PrecoTaxa.DataBaseInvalida");
    }

    // Critério 4: dataBase futura. Data derivada de UtcNow (nunca hardcoded),
    // já que o handler usa TimeProvider.System, não o FakeTimeProvider da factory.
    [Fact]
    public async Task GetPrecos_WithFutureDataBase_ShouldReturn400WithDataBaseFuturaCode()
    {
        var dataFutura = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        var response = await _client.GetAsync($"/v1/precos?dataBase={Fmt(dataFutura)}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("PrecoTaxa.DataBaseFutura");
    }

    // Critério 5: ETag/304 usando o ContentVersionProvider global (max(data_base) +
    // contagens). Prova por construção: 304 com o ETag corrente, depois nova
    // importação (preço novo em outro título/data, no passado real) muda a versão
    // e o MESMO If-None-Match antigo passa a devolver 200 com ETag diferente;
    // e dataBase diferente, mesmo sem nova importação, já produz ETag diferente.
    [Fact]
    public async Task GetPrecos_ConditionalGetFlow_ShouldReturn304ThenChangeEtagAfterNewImport_AndDifferByDataBase()
    {
        var dataBase = new DateOnly(2024, 2, 1);
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2029, 3, 1));
        await SeedPrecoAsync(tituloId, dataBase, 10m, 10m, 100m, 100m, 100m);

        var first = await _client.GetAsync($"/v1/precos?dataBase={Fmt(dataBase)}", CancellationToken.None);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull();
        var etag = first.Headers.ETag!.Tag;

        using var conditional = ConditionalGet($"/v1/precos?dataBase={Fmt(dataBase)}", etag);
        var second = await _client.SendAsync(conditional, CancellationToken.None);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        var secondBody = await second.Content.ReadAsStringAsync(CancellationToken.None);
        secondBody.Should().BeEmpty();

        var outroTituloId = await SeedTituloAsync(TipoTitulo.TesouroIPCA, new DateOnly(2032, 8, 15));
        await SeedPrecoAsync(outroTituloId, dataBase.AddDays(1), 20m, 20m, 200m, 200m, 200m);

        using var staleAfterImport = ConditionalGet($"/v1/precos?dataBase={Fmt(dataBase)}", etag);
        var afterImport = await _client.SendAsync(staleAfterImport, CancellationToken.None);
        afterImport.StatusCode.Should().Be(HttpStatusCode.OK, "a versão global de conteúdo mudou; o ETag antigo não é mais válido");
        afterImport.Headers.ETag!.Tag.Should().NotBe(etag);

        var outraDataBase = dataBase.AddDays(1);
        var comOutraDataBase = await _client.GetAsync($"/v1/precos?dataBase={Fmt(outraDataBase)}", CancellationToken.None);
        comOutraDataBase.StatusCode.Should().Be(HttpStatusCode.OK);
        comOutraDataBase.Headers.ETag!.Tag.Should().NotBe(afterImport.Headers.ETag!.Tag, "dataBase diferente compõe o ETag e deve gerar token distinto");
    }

    // Critério 6a: HEAD devolve os mesmos headers do GET (X-Total-Count, ETag,
    // Cache-Control) mas corpo vazio — herdado de MapReadGet/SuppressHeadBodyFilter.
    [Fact]
    public async Task HeadPrecos_ShouldReturn200WithSameHeadersAsGetButEmptyBody()
    {
        var dataBase = new DateOnly(2024, 3, 5);
        var tituloId = await SeedTituloAsync(TipoTitulo.TesouroSelic, new DateOnly(2029, 3, 1));
        await SeedPrecoAsync(tituloId, dataBase, 10m, 10m, 100m, 100m, 100m);

        var get = await _client.GetAsync($"/v1/precos?dataBase={Fmt(dataBase)}", CancellationToken.None);
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/v1/precos?dataBase={Fmt(dataBase)}");
        var head = await _client.SendAsync(headRequest, CancellationToken.None);

        head.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await head.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().BeEmpty();

        head.Headers.TryGetValues("X-Total-Count", out var headTotal).Should().BeTrue();
        get.Headers.TryGetValues("X-Total-Count", out var getTotal).Should().BeTrue();
        headTotal!.Single().Should().Be(getTotal!.Single());
        headTotal!.Single().Should().Be("1");

        head.Headers.ETag.Should().NotBeNull();
        // O método não entra no hash (ConditionalGetFilter.ComputeETag) — HEAD e GET
        // da mesma rota/query compartilham o mesmo ETag, como exige RFC 9110 §9.3.2.
        head.Headers.ETag!.Tag.Should().Be(get.Headers.ETag!.Tag);

        head.Headers.CacheControl.Should().NotBeNull();
        head.Headers.CacheControl!.Public.Should().BeTrue();
        head.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(300));
    }

    // Critério 6b: métodos de escrita são 405 com Allow — a rota só herda
    // GET+HEAD+OPTIONS de MapReadGet.
    [Fact]
    public async Task WritePrecos_ShouldReturn405WithAllowHeaderForAllUnsupportedMethods()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, "/v1/precos?dataBase=2024-03-05");
            var response = await _client.SendAsync(request, CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, $"{method} não é um verbo suportado por /v1/precos");
            var allow = string.Join(",", response.Content.Headers.Allow);
            allow.Should().Contain("GET");
            allow.Should().Contain("HEAD");
            allow.Should().Contain("OPTIONS");
        }
    }

    // Critério 7: fumaça de auth — sem X-Api-Key, o ApiKeyMiddleware barra antes
    // do endpoint, igual a qualquer outra rota v1.
    [Fact]
    public async Task GetPrecos_WithoutApiKey_ShouldReturn401()
    {
        using var anonymousClient = factory.CreateClient();

        var response = await anonymousClient.GetAsync("/v1/precos?dataBase=2024-03-05", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
