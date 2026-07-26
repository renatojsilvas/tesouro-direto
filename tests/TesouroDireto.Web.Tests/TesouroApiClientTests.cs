using System.Net;
using FluentAssertions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class TesouroApiClientTests
{
    private sealed record TestDto(int Id, string Nome);

    private static TesouroApiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return new TesouroApiClient(httpClient);
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
}
