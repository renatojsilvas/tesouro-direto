using System.Net;
using FluentAssertions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class TesouroApiClientTests
{
    private sealed record TestDto(int Id, string Nome);

    private static TesouroApiClient CreateClient(FakeHttpMessageHandler handler, IConditionalGetStore? store = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return new TesouroApiClient(httpClient, store ?? new BoundedConditionalGetStore());
    }

    [Fact]
    public async Task GetAsync_QuandoSucesso_DevolveObjetoDesserializado()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "itens/1", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """{"id":1,"nome":"Teste"}"""));
        var client = CreateClient(handler);

        var result = await client.GetAsync<TestDto>("itens/1");

        result.Should().Be(new TestDto(1, "Teste"));
    }

    [Fact]
    public async Task PostAsync_QuandoSucesso200ComCorpo_DevolveIsSuccessTrueEDataPreenchido()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "itens", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """{"id":42,"nome":"Criado"}"""));
        var client = CreateClient(handler);

        var result = await client.PostAsync<TestDto>("itens", new { nome = "Criado" });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(new TestDto(42, "Criado"));
        result.StatusCode.Should().Be(200);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task PostAsync_QuandoFalha400ComApiErrorJson_DevolveErrorPreenchidoEStatusCodeCorreto()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "itens", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.BadRequest, """{"code":"Item.Invalido","description":"Nome obrigatorio"}"""));
        var client = CreateClient(handler);

        var result = await client.PostAsync<TestDto>("itens", new { nome = "" });

        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeNull();
        result.StatusCode.Should().Be(400);
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("Item.Invalido");
        result.Error.Description.Should().Be("Nome obrigatorio");
    }

    [Fact]
    public async Task PostAsync_QuandoFalha400ComTextoPuroNaoParseavel_DevolveErrorNuloERawBodyPreservado()
    {
        const string corpoBruto = "erro interno";
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "itens", FakeHttpMessageHandler.TextResponse(
                HttpStatusCode.BadRequest, corpoBruto));
        var client = CreateClient(handler);

        var result = await client.PostAsync<TestDto>("itens", new { nome = "x" });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeNull();
        result.StatusCode.Should().Be(400);
        result.RawBody.Should().Be(corpoBruto);
    }

    [Fact]
    public async Task PostAsync_QuandoSucesso204SemCorpo_DevolveIsSuccessTrueEDataDefaultSemExcecao()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "itens", FakeHttpMessageHandler.NoContentResponse());
        var client = CreateClient(handler);

        var result = await client.PostAsync<TestDto>("itens", new { nome = "x" });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(default(TestDto));
        result.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task PutAsync_QuandoSucesso_DevolveIsSuccessTrueEDataPreenchido()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Put, "itens/1", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """{"id":1,"nome":"Atualizado"}"""));
        var client = CreateClient(handler);

        var result = await client.PutAsync<TestDto>("itens/1", new { nome = "Atualizado" });

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(new TestDto(1, "Atualizado"));
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task PutAsync_QuandoFalhaComApiErrorJson_DevolveErrorPreenchido()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Put, "itens/1", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.BadRequest, """{"code":"Item.NaoEncontrado","description":"Item nao existe"}"""));
        var client = CreateClient(handler);

        var result = await client.PutAsync<TestDto>("itens/1", new { nome = "x" });

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("Item.NaoEncontrado");
        result.Error.Description.Should().Be("Item nao existe");
    }

    [Fact]
    public async Task GetAsync_QuandoStoreJaTemEntradaParaAUrl_EnviaIfNoneMatch()
    {
        var store = new BoundedConditionalGetStore();
        store.Set("itens/1", new ConditionalCacheEntry(
            "\"cached-etag\"", """{"id":1,"nome":"Cache"}"""u8.ToArray(), new Dictionary<string, string>()));

        string? ifNoneMatchRecebido = null;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "itens/1", request =>
            {
                ifNoneMatchRecebido = request.Headers.IfNoneMatch.FirstOrDefault()?.Tag;
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.NotModified, "");
            });
        var client = CreateClient(handler, store);

        await client.GetAsync<TestDto>("itens/1");

        ifNoneMatchRecebido.Should().Be("\"cached-etag\"");
    }

    [Fact]
    public async Task GetAsync_QuandoRespostaTemEtag_PopulaOStore()
    {
        var store = new BoundedConditionalGetStore();
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "itens/1", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """{"id":1,"nome":"Teste"}""",
                new Dictionary<string, string> { ["ETag"] = "\"etag-1\"" }));
        var client = CreateClient(handler, store);

        await client.GetAsync<TestDto>("itens/1");

        var found = store.TryGet("itens/1", out var entry);
        found.Should().BeTrue();
        entry!.ETag.Should().Be("\"etag-1\"");
    }

    [Fact]
    public async Task GetAsync_Quando304ComEntradaEmCache_DevolveCorpoCacheadoDesserializado()
    {
        var store = new BoundedConditionalGetStore();
        var chamadas = 0;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "itens/1", request =>
            {
                chamadas++;
                return chamadas == 1
                    ? FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":1,"nome":"Original"}""",
                        new Dictionary<string, string> { ["ETag"] = "\"etag-1\"" })
                    : FakeHttpMessageHandler.JsonResponse(HttpStatusCode.NotModified, "");
            });
        var client = CreateClient(handler, store);

        var primeiraChamada = await client.GetAsync<TestDto>("itens/1");
        var segundaChamada = await client.GetAsync<TestDto>("itens/1");

        segundaChamada.Should().Be(primeiraChamada);
        chamadas.Should().Be(2);
    }

    [Fact]
    public async Task GetPagedAsync_QuandoRespostaTemHeaders_PreencheTotalCountELinks()
    {
        var store = new BoundedConditionalGetStore();
        const string linkHeader = """
            </titulos/x/precos?page=1&pageSize=25>; rel="first", </titulos/x/precos?page=1&pageSize=25>; rel="prev", </titulos/x/precos?page=3&pageSize=25>; rel="next", </titulos/x/precos?page=5&pageSize=25>; rel="last"
            """;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "titulos/x/precos", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, """[{"id":1,"nome":"A"},{"id":2,"nome":"B"}]""",
                new Dictionary<string, string>
                {
                    ["X-Total-Count"] = "42",
                    ["Link"] = linkHeader
                }));
        var client = CreateClient(handler, store);

        var result = await client.GetPagedAsync<TestDto>("titulos/x/precos?page=2&pageSize=25");

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(42);
        result.Links.First.Should().Be("/titulos/x/precos?page=1&pageSize=25");
        result.Links.Prev.Should().Be("/titulos/x/precos?page=1&pageSize=25");
        result.Links.Next.Should().Be("/titulos/x/precos?page=3&pageSize=25");
        result.Links.Last.Should().Be("/titulos/x/precos?page=5&pageSize=25");
    }

    [Fact]
    public async Task GetPagedAsync_Quando304ComEntradaEmCache_ReconstituiItemsTotalCountELinksDoCache()
    {
        var store = new BoundedConditionalGetStore();
        var chamadas = 0;
        const string linkHeader = """
            </titulos/x/precos?page=1&pageSize=25>; rel="first", </titulos/x/precos?page=5&pageSize=25>; rel="last"
            """;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "titulos/x/precos", request =>
            {
                chamadas++;
                return chamadas == 1
                    ? FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """[{"id":1,"nome":"A"}]""",
                        new Dictionary<string, string>
                        {
                            ["ETag"] = "\"etag-pagina\"",
                            ["X-Total-Count"] = "10",
                            ["Link"] = linkHeader
                        })
                    : FakeHttpMessageHandler.JsonResponse(HttpStatusCode.NotModified, "");
            });
        var client = CreateClient(handler, store);

        var primeiraChamada = await client.GetPagedAsync<TestDto>("titulos/x/precos?page=2&pageSize=25");
        var segundaChamada = await client.GetPagedAsync<TestDto>("titulos/x/precos?page=2&pageSize=25");

        segundaChamada.Items.Should().BeEquivalentTo(primeiraChamada.Items);
        segundaChamada.TotalCount.Should().Be(10);
        segundaChamada.Links.First.Should().Be("/titulos/x/precos?page=1&pageSize=25");
        segundaChamada.Links.Last.Should().Be("/titulos/x/precos?page=5&pageSize=25");
        chamadas.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_QuandoDoisClientesTransientesCompartilhamOStore_SegundoClienteReusaCacheDoPrimeiro()
    {
        var storeCompartilhado = new BoundedConditionalGetStore();
        var chamadas = 0;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "titulos", request =>
            {
                chamadas++;
                if (chamadas == 1)
                {
                    return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":1,"nome":"Teste"}""",
                        new Dictionary<string, string> { ["ETag"] = "\"etag-circuito\"" });
                }

                request.Headers.IfNoneMatch.FirstOrDefault()?.Tag.Should().Be("\"etag-circuito\"");
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.NotModified, "");
            });

        var primeiroCircuito = CreateClient(handler, storeCompartilhado);
        var segundoCircuito = CreateClient(handler, storeCompartilhado);

        var resultadoPrimeiroCircuito = await primeiroCircuito.GetAsync<TestDto>("titulos");
        var resultadoSegundoCircuito = await segundoCircuito.GetAsync<TestDto>("titulos");

        chamadas.Should().Be(2);
        resultadoSegundoCircuito.Should().Be(resultadoPrimeiroCircuito);
    }

    [Fact]
    public async Task GetAsync_QuandoRespostaNaoE2xx_LancaExcecao()
    {
        var store = new BoundedConditionalGetStore();
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "itens/1", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.InternalServerError, "{}"));
        var client = CreateClient(handler, store);

        var act = () => client.GetAsync<TestDto>("itens/1");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
