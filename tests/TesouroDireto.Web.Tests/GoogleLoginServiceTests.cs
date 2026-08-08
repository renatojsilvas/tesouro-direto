using System.Net;
using System.Text.Json;
using FluentAssertions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class GoogleLoginServiceTests
{
    private static TesouroApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return new TesouroApiClient(httpClient, new BoundedConditionalGetStore());
    }

    [Fact]
    public async Task ProcessLoginAsync_QuandoEmailNaoVerificado_NaoChamaSyncEDevolveFalso()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new GoogleLoginService(CreateClient(handler));
        var claims = new GoogleLoginClaims("google-sub-1", "usuario@teste.com", "Usuario Teste", false);

        var resultado = await service.ProcessLoginAsync(claims);

        resultado.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessLoginAsync_QuandoEmailVerificado_ChamaSyncComBodyCorreto()
    {
        HttpRequestMessage? requisicaoCapturada = null;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "v1/admin/usuarios/sync", request =>
            {
                requisicaoCapturada = request;
                return FakeHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK, """{"id":"11111111-1111-1111-1111-111111111111","aprovado":false,"papel":"User"}""");
            });
        var service = new GoogleLoginService(CreateClient(handler));
        var claims = new GoogleLoginClaims("google-sub-2", "novo@teste.com", "Novo Usuario", true);

        var resultado = await service.ProcessLoginAsync(claims);

        resultado.Ok.Should().BeTrue();
        resultado.Papel.Should().Be("User");
        requisicaoCapturada.Should().NotBeNull();
        requisicaoCapturada!.Method.Should().Be(HttpMethod.Post);

        var corpoCapturado = await requisicaoCapturada.Content!.ReadAsStringAsync();
        var corpo = JsonSerializer.Deserialize<JsonElement>(corpoCapturado);
        corpo.GetProperty("googleSub").GetString().Should().Be("google-sub-2");
        corpo.GetProperty("email").GetString().Should().Be("novo@teste.com");
        corpo.GetProperty("nome").GetString().Should().Be("Novo Usuario");
        corpo.GetProperty("emailVerified").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ProcessLoginAsync_QuandoSyncRetorna400_DevolveFalso()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Post, "v1/admin/usuarios/sync", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.BadRequest, """{"code":"Usuario.EmailNaoVerificado","detail":"E-mail nao verificado"}"""));
        var service = new GoogleLoginService(CreateClient(handler));
        var claims = new GoogleLoginClaims("google-sub-3", "falha@teste.com", "Falha Usuario", true);

        var resultado = await service.ProcessLoginAsync(claims);

        resultado.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessLoginAsync_QuandoFalhaDeRede_DevolveFalso()
    {
        var handler = new ThrowingHttpMessageHandler();
        var service = new GoogleLoginService(CreateClient(handler));
        var claims = new GoogleLoginClaims("google-sub-4", "rede@teste.com", "Rede Usuario", true);

        var resultado = await service.ProcessLoginAsync(claims);

        resultado.Ok.Should().BeFalse();
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Falha de rede simulada");
    }
}
