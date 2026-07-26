using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TesouroDireto.API.Extensions;

namespace TesouroDireto.API.Tests.Integration;

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

            paths.TryGetProperty("/", out _).Should().BeFalse(
                "a rota \"/\" foi marcada com ExcludeFromDescription e não deveria aparecer no doc.");

            var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
            var apiKeyScheme = securitySchemes.GetProperty("ApiKey");
            apiKeyScheme.GetProperty("in").GetString().Should().Be("header");
            apiKeyScheme.GetProperty("name").GetString().Should().Be("X-Api-Key");

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

            var schemas = root.GetProperty("components").GetProperty("schemas");
            var createTributoRequestSchema = schemas.EnumerateObject()
                .FirstOrDefault(p => p.Name.Contains("CreateTributoRequest", StringComparison.Ordinal));
            createTributoRequestSchema.Value.ValueKind.Should().NotBe(JsonValueKind.Undefined,
                $"deveria existir um schema para CreateTributoRequest.\n{body}");

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

        [Fact]
        public async Task GetSwaggerJson_ShouldMarkIdBasedRoutesAndIdFieldAsDeprecated()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var paths = root.GetProperty("paths");

            foreach (var idBasedPath in new[] { "/titulos/{id}/precos", "/titulos/{id}/preco-atual" })
            {
                var operation = paths.GetProperty(idBasedPath).GetProperty("get");
                operation.GetProperty("deprecated").GetBoolean().Should().BeTrue(
                    $"a operação {idBasedPath} deveria estar marcada como deprecated no swagger.json.\n{body}");
            }

            foreach (var codigoBasedPath in new[] { "/titulos/{codigo}/precos", "/titulos/{codigo}/preco-atual" })
            {
                var operation = paths.GetProperty(codigoBasedPath).GetProperty("get");
                operation.TryGetProperty("deprecated", out var deprecatedFlag).Should().BeFalse(
                    $"a operação {codigoBasedPath} não deveria estar marcada como deprecated.\n{deprecatedFlag}");
            }

            var tituloDtoSchema = root.GetProperty("components").GetProperty("schemas").GetProperty("TituloDto");
            var idProperty = tituloDtoSchema.GetProperty("properties").GetProperty("id");
            idProperty.GetProperty("deprecated").GetBoolean().Should().BeTrue(
                $"a propriedade id do TituloDto deveria estar marcada como deprecated no schema.\n{tituloDtoSchema}");
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
        public async Task GetSwaggerJson_WithoutApiKey_ShouldReturn200()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
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
        public async Task GetSwaggerUi_WithoutApiKey_ShouldReturn200()
        {
            var response = await _client.GetAsync("/swagger/index.html", CancellationToken.None);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Metrics_ShouldNotIncludeSwaggerPathInHttpMetricsSeries()
        {
            var emptyBefore = await ScrapeHttpMetricSumAsync("endpoint=\"\"");

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json"))
            {
                request.Headers.Add("X-Api-Key", ValidApiKey);
                var swaggerResponse = await _client.SendAsync(request, CancellationToken.None);
                swaggerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var emptyAfterSwagger = await ScrapeHttpMetricSumAsync("endpoint=\"\"");
            emptyAfterSwagger.Should().Be(emptyBefore,
                "hits em /swagger são excluídos do UseHttpMetrics e não devem incrementar nenhuma série "
                + "http_* com endpoint=\"\" (mesmo tratamento dado a /health*+/metrics na tarefa 29). "
                + "A asserção é por DELTA e não por ausência absoluta porque o registry do prometheus-net "
                + "é estático/global do processo — outras classes de teste (ex.: 401 do ApiKeyMiddleware, "
                + "que curto-circuita antes do roteamento) já podem ter produzido endpoint=\"\".");

            var throwBefore = await ScrapeHttpMetricSumAsync("endpoint=\"/_test/throw\"");

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/_test/throw"))
            {
                request.Headers.Add("X-Api-Key", ValidApiKey);
                await _client.SendAsync(request, CancellationToken.None);
            }

            var throwAfter = await ScrapeHttpMetricSumAsync("endpoint=\"/_test/throw\"");
            throwAfter.Should().BeGreaterThan(throwBefore,
                "positivo de controle: a rota roteada /_test/throw É instrumentada, provando que "
                + "UseHttpMetrics está ativo e o scrape funciona (senão o delta acima seria vácuo).");
        }

        private async Task<double> ScrapeHttpMetricSumAsync(string labelToken)
        {
            var response = await _client.GetAsync("/metrics", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            double sum = 0;
            foreach (var line in body.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (!line.StartsWith("http_request_duration_seconds_count", StringComparison.Ordinal)
                    && !line.StartsWith("http_requests_received_total", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!line.Contains(labelToken, StringComparison.Ordinal))
                {
                    continue;
                }

                var lastSpace = line.LastIndexOf(' ');
                if (lastSpace >= 0 && double.TryParse(
                        line[(lastSpace + 1)..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var value))
                {
                    sum += value;
                }
            }

            return sum;
        }

        public sealed class SwaggerProdFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ApiKey:Key"] = ValidApiKey,
                        ["ApiKey:ExcludedPaths:0"] = "/health",
                        ["ApiKey:ExcludedPaths:1"] = "/metrics",
                        ["ApiKey:ExcludedPaths:2"] = "/swagger",
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
