using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TesouroDireto.API.Extensions;

namespace TesouroDireto.API.Tests.Integration;

/// <summary>
/// Tarefa 33: geração/exposição do OpenAPI/Swagger. Hosts leves (connstring FAKE, sem
/// Testcontainers) — geração do swagger.json não toca o banco. Em "Development", o boot
/// tentaria migração real via <see cref="IDatabaseInitializer"/> mesmo com uma connstring
/// fake, então ele é substituído por um no-op só nesses testes.
/// </summary>
public sealed class SwaggerEndpointTests
{
    private const string FakeConnectionString =
        "Host=localhost;Database=fake;Username=fake;Password=fake";

    public sealed class DevelopmentTests : IClassFixture<DevelopmentTests.SwaggerDevFactory>
    {
        private readonly HttpClient _client;

        public DevelopmentTests(SwaggerDevFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetSwaggerJson_InDevelopment_ShouldReturn200WithoutApiKey()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GetSwaggerJson_ShouldDescribeBusinessRoutesSecuritySchemeAndStringEnums()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var paths = root.GetProperty("paths");
            foreach (var expectedPath in new[]
                     {
                         "/titulos", "/simulador", "/configuracoes/tributos", "/importacao",
                     })
            {
                paths.TryGetProperty(expectedPath, out _).Should().BeTrue(
                    $"o path {expectedPath} deveria estar documentado no swagger.json.\n{body}");
            }

            // "/" é infraestrutura de smoke-test, não rota de negócio — ExcludeFromDescription.
            paths.TryGetProperty("/", out _).Should().BeFalse(
                "a rota \"/\" foi marcada com ExcludeFromDescription e não deveria aparecer no doc.");

            var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
            var apiKeyScheme = securitySchemes.GetProperty("ApiKey");
            apiKeyScheme.GetProperty("in").GetString().Should().Be("header");
            apiKeyScheme.GetProperty("name").GetString().Should().Be("X-Api-Key");

            // AddSecurityRequirement é um requisito GLOBAL do documento (chave "security" na
            // raiz do OpenAPI) — pela spec, isso já se aplica a TODAS as operations, sem
            // precisar repetir "security" em cada uma individualmente.
            root.TryGetProperty("security", out var documentSecurity).Should().BeTrue(
                $"o documento deveria ter um security requirement global referenciando ApiKey.\n{body}");
            var referencesApiKeyScheme = documentSecurity.EnumerateArray()
                .Any(req => req.TryGetProperty("ApiKey", out var apiKeyRequirement)
                    && apiKeyRequirement.ValueKind == JsonValueKind.Array);
            referencesApiKeyScheme.Should().BeTrue(
                $"o security requirement global deveria referenciar o scheme ApiKey.\n{documentSecurity}");

            var titulosOperation = paths.GetProperty("/titulos").GetProperty("get");
            titulosOperation.GetProperty("responses").TryGetProperty("401", out var response401)
                .Should().BeTrue("a operation de /titulos deveria documentar 401.");
            response401.GetProperty("content").TryGetProperty("application/problem+json", out _)
                .Should().BeTrue("a resposta 401 deveria usar content-type application/problem+json.");

            // BaseCalculo/TipoCalculo (enums de CreateTributoRequest) devem aparecer como string,
            // não como integer — Swashbuckle 6.x não lê JsonStringEnumConverter registrado via
            // ConfigureHttpJsonOptions (Minimal API), só via MVC; ver Program.cs para a solução.
            var schemas = root.GetProperty("components").GetProperty("schemas");
            var createTributoRequestSchema = schemas.EnumerateObject()
                .FirstOrDefault(p => p.Name.Contains("CreateTributoRequest", StringComparison.Ordinal));
            createTributoRequestSchema.Value.ValueKind.Should().NotBe(JsonValueKind.Undefined,
                $"deveria existir um schema para CreateTributoRequest.\n{body}");

            // As propriedades referenciam os schemas dos enums via $ref — o type:string real
            // fica no schema nomeado (BaseCalculo/TipoCalculo), não inline na propriedade.
            createTributoRequestSchema.Value.GetProperty("properties").GetProperty("baseCalculo")
                .GetProperty("$ref").GetString().Should().Be("#/components/schemas/BaseCalculo");
            createTributoRequestSchema.Value.GetProperty("properties").GetProperty("tipoCalculo")
                .GetProperty("$ref").GetString().Should().Be("#/components/schemas/TipoCalculo");

            var baseCalculoSchema = schemas.GetProperty("BaseCalculo");
            baseCalculoSchema.GetProperty("type").GetString().Should().Be("string",
                $"BaseCalculo deveria serializar como string no schema, não como integer.\n{baseCalculoSchema}");
            baseCalculoSchema.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
                .Should().Contain("Rendimento");

            var tipoCalculoSchema = schemas.GetProperty("TipoCalculo");
            tipoCalculoSchema.GetProperty("type").GetString().Should().Be("string",
                $"TipoCalculo deveria serializar como string no schema, não como integer.\n{tipoCalculoSchema}");
            tipoCalculoSchema.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
                .Should().Contain("FaixaPorDias");
        }

        [Fact]
        public async Task GetSwaggerUi_InDevelopment_ShouldReturn200()
        {
            var response = await _client.GetAsync("/swagger/index.html", CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public sealed class SwaggerDevFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ApiKey:Key"] = "test-key",
                        ["ApiKey:ExcludedPaths:0"] = "/health",
                        ["ApiKey:ExcludedPaths:1"] = "/metrics",
                        ["ConnectionStrings:DefaultConnection"] = FakeConnectionString,
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Development tentaria migração real no boot (DatabaseInitializer só
                    // pula sob o environment "Testing"); com uma connstring fake isso
                    // quebraria o host antes de qualquer teste rodar.
                    services.RemoveAll<IDatabaseInitializer>();
                    services.AddSingleton<IDatabaseInitializer, NoOpDatabaseInitializer>();
                });
            }
        }
    }

    public sealed class ProductionLikeTests : IClassFixture<ProductionLikeTests.SwaggerProdFactory>
    {
        private const string ValidApiKey = "prod-like-swagger-test-key";
        private readonly HttpClient _client;

        public ProductionLikeTests(SwaggerProdFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetSwaggerJson_WithoutApiKey_ShouldReturn401()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task GetSwaggerJson_WithApiKey_ShouldReturn200()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
            request.Headers.Add("X-Api-Key", ValidApiKey);

            var response = await _client.SendAsync(request, CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GetSwaggerUi_ShouldReturn404()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/index.html");
            request.Headers.Add("X-Api-Key", ValidApiKey);

            var response = await _client.SendAsync(request, CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Metrics_ShouldNotIncludeSwaggerPathInHttpMetricsSeries()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json"))
            {
                request.Headers.Add("X-Api-Key", ValidApiKey);
                var swaggerResponse = await _client.SendAsync(request, CancellationToken.None);
                swaggerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // Positivo de controle: rota ROTEADA de verdade (diferente do middleware puro do
            // swagger), instrumentada com endpoint="/_test/throw" — prova que a instrumentação
            // está ativa e o scrape funciona (senão a ausência de série vazia abaixo seria
            // vácua por o scrape inteiro estar quebrado). Usa /_test/throw (só mapeada sob
            // "Testing", que é o environment desta fixture) em vez de uma rota de negócio real
            // para não depender do Postgres (connstring desta fixture é fake).
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/_test/throw"))
            {
                request.Headers.Add("X-Api-Key", ValidApiKey);
                await _client.SendAsync(request, CancellationToken.None);
            }

            var metricsResponse = await _client.GetAsync("/metrics", CancellationToken.None);
            var body = await metricsResponse.Content.ReadAsStringAsync(CancellationToken.None);

            foreach (var series in new[] { "http_request_duration_seconds_count", "http_requests_received_total" })
            {
                // UseSwagger() é middleware puro (não um endpoint roteado) — quando
                // instrumentado por engano pelo UseHttpMetrics, o prometheus-net não tem como
                // saber o path da rota e emite endpoint="" (vazio), nunca "endpoint=/swagger...".
                // A asserção correta é a ausência do label vazio, não de um path "/swagger*"
                // que o prometheus-net jamais produziria.
                var emptyEndpointPattern = new Regex(
                    $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""""[^}}]*\}}");

                emptyEndpointPattern.IsMatch(body).Should().BeFalse(
                    $"a série {series} não deveria conter endpoint=\"\" (label vazio emitido pelo " +
                    $"middleware do swagger quando não excluído do UseHttpMetrics — mesmo tratamento " +
                    $"dado a /health*+/metrics na tarefa 29).\nCorpo:\n{body}");

                var controlPattern = new Regex(
                    $@"{Regex.Escape(series)}\{{[^}}]*endpoint=""/_test/throw""[^}}]*\}}");

                controlPattern.IsMatch(body).Should().BeTrue(
                    $"a rota roteada /_test/throw deveria continuar instrumentada com seu " +
                    $"endpoint real (positivo de controle: prova que a instrumentação está ativa e " +
                    $"o scrape funciona).\nCorpo:\n{body}");
            }
        }

        public sealed class SwaggerProdFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                // "Testing" pula a migração nativamente (DatabaseInitializer.InitializeAsync
                // retorna cedo) — não precisa de no-op aqui. Representa o comportamento de
                // produção para o pipeline do swagger (UseSwagger depois do ApiKeyMiddleware,
                // sem UseSwaggerUI) porque não é Development.
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ApiKey:Key"] = ValidApiKey,
                        ["ApiKey:ExcludedPaths:0"] = "/health",
                        ["ApiKey:ExcludedPaths:1"] = "/metrics",
                        ["ConnectionStrings:DefaultConnection"] = FakeConnectionString,
                    });
                });
            }
        }
    }

    private sealed class NoOpDatabaseInitializer : IDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
