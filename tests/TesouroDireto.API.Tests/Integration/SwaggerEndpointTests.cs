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
                         "/v1/titulos", "/v1/simulador", "/v1/configuracoes/tributos", "/v1/importacao",
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

            var titulosOperation = paths.GetProperty("/v1/titulos").GetProperty("get");
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
        public async Task GetSwaggerJson_ShouldNotExposeIdBasedRoutesOrIdField()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var paths = root.GetProperty("paths");

            foreach (var idBasedPath in new[] { "/v1/titulos/{id}/precos", "/v1/titulos/{id}/preco-atual" })
            {
                paths.TryGetProperty(idBasedPath, out _).Should().BeFalse(
                    $"a rota {idBasedPath} foi removida na tarefa 38 e não deveria aparecer no swagger.json.\n{body}");
            }

            var tituloResourceSchema = root.GetProperty("components").GetProperty("schemas").GetProperty("TituloResource");
            tituloResourceSchema.GetProperty("properties").TryGetProperty("id", out _).Should().BeFalse(
                $"o campo id foi removido do contrato público na tarefa 38 e não deveria aparecer no schema.\n{tituloResourceSchema}");
        }

        [Fact]
        public async Task GetSwaggerJson_ShouldDocumentPaginationParamsAndRestMaturityHeaders()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var paths = root.GetProperty("paths");

            var precosPorCodigoGet = paths.GetProperty("/v1/titulos/{codigo}/precos").GetProperty("get");
            var parameterNames = precosPorCodigoGet.GetProperty("parameters").EnumerateArray()
                .Select(p => p.GetProperty("name").GetString())
                .ToList();
            parameterNames.Should().Contain("page", $"page deveria estar documentado.\n{body}");
            parameterNames.Should().Contain("pageSize", $"pageSize deveria estar documentado.\n{body}");

            var okResponseHeaders = precosPorCodigoGet.GetProperty("responses").GetProperty("200").GetProperty("headers");
            okResponseHeaders.TryGetProperty("ETag", out _).Should().BeTrue($"ETag deveria estar documentado.\n{body}");
            okResponseHeaders.TryGetProperty("X-Total-Count", out _).Should().BeTrue(
                $"X-Total-Count deveria estar documentado.\n{body}");
            okResponseHeaders.TryGetProperty("Link", out _).Should().BeTrue($"Link deveria estar documentado.\n{body}");

            var createTributoPost = paths.GetProperty("/v1/configuracoes/tributos").GetProperty("post");
            var createdResponseHeaders = createTributoPost.GetProperty("responses")
                .GetProperty("201").GetProperty("headers");
            createdResponseHeaders.TryGetProperty("Location", out _).Should().BeTrue(
                $"Location deveria estar documentado.\n{body}");

            var titulosGet = paths.GetProperty("/v1/titulos").GetProperty("get");
            var titulosSchema = titulosGet.GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema");
            titulosSchema.GetProperty("items").GetProperty("$ref").GetString()
                .Should().Be("#/components/schemas/TituloResource");

            var tituloResourceSchema = root.GetProperty("components").GetProperty("schemas").GetProperty("TituloResource");
            tituloResourceSchema.GetProperty("properties").TryGetProperty("_links", out _).Should().BeTrue(
                $"_links deveria estar documentado no schema de TituloResource.\n{tituloResourceSchema}");
        }

        [Theory]
        [InlineData("/v1/precos")]
        [InlineData("/v1/titulos/{codigo}/precos")]
        public async Task GetSwaggerJson_ShouldNotDocumentQueryParameterTwice(string path)
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var getOperation = root.GetProperty("paths").GetProperty(path).GetProperty("get");

            var duplicatedNames = getOperation.GetProperty("parameters").EnumerateArray()
                .Where(p => p.GetProperty("in").GetString() == "query")
                .GroupBy(p => p.GetProperty("name").GetString())
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            duplicatedNames.Should().BeEmpty(
                $"o(s) parâmetro(s) de query {string.Join(", ", duplicatedNames)} aparece(m) mais de uma vez em " +
                $"{path} — o operation filter deveria enriquecer o parâmetro que o ApiExplorer já descobriu, " +
                $"não adicionar uma entrada duplicada.\n{body}");
        }

        [Fact]
        public async Task GetSwaggerJson_ShouldEnrichExistingQueryParametersWithoutDuplicating()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var paths = root.GetProperty("paths");

            var precosGet = paths.GetProperty("/v1/precos").GetProperty("get");
            var dataBaseParam = precosGet.GetProperty("parameters").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == "dataBase");
            dataBaseParam.GetProperty("required").GetBoolean().Should().BeTrue(
                $"dataBase deveria continuar obrigatório após o enriquecimento.\n{body}");
            dataBaseParam.GetProperty("schema").GetProperty("format").GetString().Should().Be("date",
                $"dataBase deveria continuar documentado com format=date.\n{body}");

            var precosPorCodigoGet = paths.GetProperty("/v1/titulos/{codigo}/precos").GetProperty("get");
            var queryParams = precosPorCodigoGet.GetProperty("parameters").EnumerateArray()
                .Where(p => p.GetProperty("in").GetString() == "query")
                .ToList();

            var pageParam = queryParams.Single(p => p.GetProperty("name").GetString() == "page");
            pageParam.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(
                $"page deveria continuar com descrição após o enriquecimento.\n{body}");
            pageParam.GetProperty("schema").GetProperty("format").GetString().Should().Be("int32",
                $"o enriquecimento não pode descartar o format que o ApiExplorer já tinha inferido.\n{body}");

            var pageSizeParam = queryParams.Single(p => p.GetProperty("name").GetString() == "pageSize");
            pageSizeParam.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(
                $"pageSize deveria continuar com descrição após o enriquecimento.\n{body}");
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

            // Positivo de controle via code="500", não via endpoint="/_test/throw": desde que
            // UseHttpMetrics passou a envolver o UseExceptionHandler (fix do label `code`
            // reportar o status real em requisições com exceção — ver
            // HttpMetricsExceptionOrderingTests), o UseExceptionHandler limpa o endpoint
            // resolvido (context.SetEndpoint(null)) antes de reescrever a resposta para 5xx,
            // então a série final gravada para /_test/throw carrega endpoint="" (não mais o
            // path). O código de status, porém, é gravado corretamente como 500 — e é esse o
            // sinal que prova que UseHttpMetrics está ativo e o scrape não é vácuo.
            var throwBefore = await ScrapeHttpMetricSumAsync("code=\"500\"");

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/_test/throw"))
            {
                request.Headers.Add("X-Api-Key", ValidApiKey);
                await _client.SendAsync(request, CancellationToken.None);
            }

            var throwAfter = await ScrapeHttpMetricSumAsync("code=\"500\"");
            throwAfter.Should().BeGreaterThan(throwBefore,
                "positivo de controle: a rota roteada /_test/throw É instrumentada e o label code "
                + "reflete o status real (500), provando que UseHttpMetrics está ativo e o scrape "
                + "funciona (senão o delta acima seria vácuo).");
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
