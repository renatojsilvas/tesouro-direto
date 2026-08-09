using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class OpenApiDocumentServiceTests
{
    private const string PublicBaseUrl = "https://dadosdotesourodireto.com.br/api/v1";

    private const string FixtureJson = """
        {
          "openapi": "3.0.1",
          "servers": [ { "url": "/" } ],
          "paths": {
            "/v1/titulos": {
              "get": {
                "responses": {
                  "200": {
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/TituloResource" }
                      }
                    }
                  }
                }
              }
            },
            "/v1/titulos/{codigo}/preco-atual": { "get": {} },
            "/v1/mercado": { "get": {} },
            "/v1/me/keys": {
              "get": {
                "responses": {
                  "200": {
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/ApiKeyDto" }
                      }
                    }
                  }
                }
              }
            },
            "/v1/admin/usuarios": { "get": {} }
          },
          "components": {
            "schemas": {
              "ApiKeyDto": { "type": "object" },
              "TituloResource": {
                "type": "object",
                "properties": {
                  "_links": { "$ref": "#/components/schemas/HateoasLink" }
                }
              },
              "HateoasLink": { "type": "object" }
            }
          }
        }
        """;

    private static OpenApiDocumentService CreateService(
        FakeHttpMessageHandler handler,
        IMemoryCache? cache = null,
        IConfiguration? configuration = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var apiClient = new TesouroApiClient(httpClient, new BoundedConditionalGetStore());
        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSettings:PublicBaseUrl"] = PublicBaseUrl
            })
            .Build();

        return new OpenApiDocumentService(
            apiClient,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            config,
            NullLogger<OpenApiDocumentService>.Instance);
    }

    [Fact]
    public void Transform_MantemRotasPublicas()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        var paths = JsonNode.Parse(resultado)!.AsObject()["paths"]!.AsObject();

        paths.ContainsKey("/titulos").Should().BeTrue();
        paths.ContainsKey("/titulos/{codigo}/preco-atual").Should().BeTrue();
    }

    [Fact]
    public void Transform_ComposicaoDeServerUrlComPaths_NaoDuplicaPrefixoV1()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        var documento = JsonNode.Parse(resultado)!.AsObject();
        var serverUrl = documento["servers"]!.AsArray()[0]!["url"]!.GetValue<string>();
        var paths = documento["paths"]!.AsObject();

        var chaveTitulos = paths.Select(par => par.Key)
            .Single(chave => chave.EndsWith("/titulos", StringComparison.Ordinal));
        var chavePrecoAtual = paths.Select(par => par.Key)
            .Single(chave => chave.EndsWith("/preco-atual", StringComparison.Ordinal));

        (serverUrl + chaveTitulos).Should().Be("https://dadosdotesourodireto.com.br/api/v1/titulos");
        (serverUrl + chavePrecoAtual).Should().Be("https://dadosdotesourodireto.com.br/api/v1/titulos/{codigo}/preco-atual");
    }

    [Fact]
    public void Transform_PathComPrefixoParcialSemelhanteAMeOuAdmin_NaoEhRemovidoPorFalsoPositivo()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        var paths = JsonNode.Parse(resultado)!.AsObject()["paths"]!.AsObject();

        paths.Any(par => par.Key.Contains("mercado", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        paths.Any(par => par.Key.Contains("me/keys", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Fact]
    public void Transform_PodaSchemasNaoAlcancaveis_MantemApenasOsAlcancaveisTransitivamente()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        var schemas = JsonNode.Parse(resultado)!.AsObject()["components"]!["schemas"]!.AsObject();

        schemas.ContainsKey("TituloResource").Should().BeTrue();
        schemas.ContainsKey("HateoasLink").Should().BeTrue();
        schemas.ContainsKey("ApiKeyDto").Should().BeFalse();
    }

    [Fact]
    public void Transform_RemoveRotasDeMeEDeAdmin()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        resultado.Should().NotContain("/v1/me/keys");

        var paths = JsonNode.Parse(resultado)!.AsObject()["paths"]!.AsObject();
        paths.Any(par => par.Key.StartsWith("/v1/admin", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReescreveServersParaAUrlPublica()
    {
        var resultado = OpenApiDocumentService.Transform(FixtureJson, PublicBaseUrl);

        var servers = JsonNode.Parse(resultado)!.AsObject()["servers"]!.AsArray();

        servers.Should().HaveCount(1);
        servers[0]!["url"]!.GetValue<string>().Should().Be(PublicBaseUrl);
    }

    [Fact]
    public async Task GetDocumentAsync_QuandoApiRespondeComSucesso_DevolveDocumentoFiltrado()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "swagger/v1/swagger.json", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK, FixtureJson));
        var service = CreateService(handler);

        var documento = await service.GetDocumentAsync();

        documento.Should().NotBeNull();
        documento.Should().Contain("\"/titulos\"");
        documento.Should().NotContain("/v1/me/keys");
    }

    [Fact]
    public async Task GetDocumentAsync_QuandoApiFalha500_DevolveNuloSemLancar()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "swagger/v1/swagger.json", FakeHttpMessageHandler.JsonResponse(
                HttpStatusCode.InternalServerError, "{}"));
        var service = CreateService(handler);

        var documento = await service.GetDocumentAsync();

        documento.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentAsync_ChamadoDuasVezes_UsaCacheNaSegundaChamada()
    {
        var chamadas = 0;
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "swagger/v1/swagger.json", _ =>
            {
                chamadas++;
                return chamadas == 1
                    ? FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, FixtureJson)
                    : FakeHttpMessageHandler.JsonResponse(HttpStatusCode.InternalServerError, "{}");
            });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(handler, cache);

        var primeiraChamada = await service.GetDocumentAsync();
        var segundaChamada = await service.GetDocumentAsync();

        chamadas.Should().Be(1);
        segundaChamada.Should().Be(primeiraChamada);
    }

    private sealed class WebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }

    [Fact]
    public async Task Referencia_QuandoAnonimo_NaoDevolveSwaggerUiEExigeChallenge()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/desenvolvedores/referencia/index.html");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        var corpo = await response.Content.ReadAsStringAsync();
        corpo.Should().NotContain("swagger-ui");
    }

    [Fact]
    public async Task OpenApiJson_QuandoAnonimo_NaoDevolveDocumento()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/desenvolvedores/openapi.json");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
