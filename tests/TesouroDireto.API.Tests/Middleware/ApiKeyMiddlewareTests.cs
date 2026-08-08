using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Prometheus;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TesouroDireto.API.Middleware;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Observability;
using TesouroDireto.Infrastructure.RateLimiting;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class ApiKeyMiddlewareTests : IClassFixture<ApiKeyMiddlewareTests.ApiKeyWebFactory>
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ValidApiKey = "test-api-key-12345";
    private const string ActiveClientKey = "client-active-key-0123456789";
    private const string UnknownClientKey = "client-unknown-key-0123456789";

    private static readonly Counter ApiKeyRequestsTotal = Metrics.CreateCounter(
        "api_key_requests_total", "help", new CounterConfiguration { LabelNames = ["cliente", "outcome"] });

    private readonly ApiKeyWebFactory _factory;
    private readonly HttpClient _client;

    public ApiKeyMiddlewareTests(ApiKeyWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithInvalidApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, "wrong-key");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithValidApiKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ValidApiKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithEmptyApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, string.Empty);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithWhitespaceApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, "   ");

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_ToExcludedPath_WithoutApiKey_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_ToNonExcludedSimilarPath_WithoutApiKey_ShouldReturn401()
    {
        var response = await _client.GetAsync("/healthz", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_ToSwaggerPath_WithoutApiKey_ShouldReturn200()
    {
        var response = await _client.GetAsync("/swagger/index.html", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithActiveClientApiKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ActiveClientKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithUnknownClientApiKey_ShouldReturn401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, UnknownClientKey);

        var response = await _client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithActiveClientApiKey_ShouldSetClienteIdToApiKeyIdOnDiagnosticContext()
    {
        _factory.DiagnosticContext.Properties.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ActiveClientKey);

        await _client.SendAsync(request, CancellationToken.None);

        _factory.DiagnosticContext.Properties.Should()
            .Contain(("ClienteId", (object)ApiKeyWebFactory.ActiveClientApiKeyId.ToString()));
    }

    [Fact]
    public async Task Request_WithServiceKey_ShouldSetClienteIdToServiceOnDiagnosticContext()
    {
        _factory.DiagnosticContext.Properties.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ValidApiKey);

        await _client.SendAsync(request, CancellationToken.None);

        _factory.DiagnosticContext.Properties.Should().Contain(("ClienteId", (object)"service"));
    }

    [Fact]
    public async Task Request_WithActiveClientApiKey_ShouldIncrementMetricForApiKeyIdAuthorized()
    {
        var cliente = ApiKeyWebFactory.ActiveClientApiKeyId.ToString();
        var before = ApiKeyRequestsTotal.WithLabels(cliente, "authorized").Value;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ActiveClientKey);
        await _client.SendAsync(request, CancellationToken.None);

        var after = ApiKeyRequestsTotal.WithLabels(cliente, "authorized").Value;
        (after - before).Should().Be(1);
    }

    [Fact]
    public async Task Request_WithServiceKey_ShouldIncrementMetricForServiceAuthorized()
    {
        var before = ApiKeyRequestsTotal.WithLabels("service", "authorized").Value;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, ValidApiKey);
        await _client.SendAsync(request, CancellationToken.None);

        var after = ApiKeyRequestsTotal.WithLabels("service", "authorized").Value;
        (after - before).Should().Be(1);
    }

    [Fact]
    public async Task Request_WithUnknownClientApiKey_ShouldIncrementMetricForUnknownUnauthorized()
    {
        var before = ApiKeyRequestsTotal.WithLabels("unknown", "unauthorized").Value;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ApiKeyHeader, UnknownClientKey);
        await _client.SendAsync(request, CancellationToken.None);

        var after = ApiKeyRequestsTotal.WithLabels("unknown", "unauthorized").Value;
        (after - before).Should().Be(1);
    }

    [Fact]
    public async Task Request_WithoutApiKey_ShouldIncrementMetricForUnknownUnauthorized()
    {
        var before = ApiKeyRequestsTotal.WithLabels("unknown", "unauthorized").Value;

        await _client.GetAsync("/", CancellationToken.None);

        var after = ApiKeyRequestsTotal.WithLabels("unknown", "unauthorized").Value;
        (after - before).Should().Be(1);
    }

    public sealed class ApiKeyWebFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid ActiveClientApiKeyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public CapturingDiagnosticContext DiagnosticContext { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = ValidApiKey,
                    ["ApiKey:ExcludedPaths:0"] = "/health",
                    ["ApiKey:ExcludedPaths:1"] = "/swagger",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                var activeByHash = new Dictionary<string, ApiKeyDto>
                {
                    [ApiKeyHash.FromRawKey(ActiveClientKey).Value] = new(
                        ActiveClientApiKeyId,
                        "Cliente Ativo",
                        "clie1234",
                        Guid.NewGuid(),
                        true,
                        DateTimeOffset.UtcNow,
                        null)
                };

                services.RemoveAll<IApiKeyReadRepository>();
                services.AddScoped<IApiKeyReadRepository>(_ => new FakeApiKeyReadRepository(activeByHash));

                services.RemoveAll<IDiagnosticContext>();
                services.AddSingleton<IDiagnosticContext>(DiagnosticContext);
            });
        }
    }

    public sealed class CapturingDiagnosticContext : IDiagnosticContext
    {
        public ConcurrentBag<(string Name, object? Value)> Properties { get; } = new();

        public void Set(string propertyName, object? value, bool destructureObjects = false) =>
            Properties.Add((propertyName, value));

        public void SetException(Exception? exception)
        {
        }
    }

    private sealed class FakeApiKeyReadRepository(IReadOnlyDictionary<string, ApiKeyDto> activeByHash) : IApiKeyReadRepository
    {
        public Task<Result<ApiKeyDto>> GetByHashAsync(string hash, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ApiKeyDto>.Failure(ApiKeyErrors.NotFound));

        public Task<Result<ApiKeyDto>> GetActiveByHashAsync(string hash, CancellationToken cancellationToken) =>
            Task.FromResult(activeByHash.TryGetValue(hash, out var dto)
                ? Result<ApiKeyDto>.Success(dto)
                : Result<ApiKeyDto>.Failure(ApiKeyErrors.NotFound));

        public Task<Result<IReadOnlyCollection<ApiKeyDto>>> ListByDonoAsync(Guid donoUsuarioId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyCollection<ApiKeyDto>>.Success((IReadOnlyCollection<ApiKeyDto>)Array.Empty<ApiKeyDto>()));
    }
}

public sealed class ApiKeyMiddlewareLogContextTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ServiceKey = "service-key-for-logcontext-test";
    private const string ClientKey = "client-key-for-logcontext-test";
    private static readonly Guid ClientApiKeyId = Guid.NewGuid();

    [Fact]
    public async Task InvokeAsync_WithServiceKey_ShouldPushClienteIdServiceDuringNextOnly()
    {
        IReadOnlyDictionary<string, LogEventPropertyValue>? duringNext = null;
        var context = CreateHttpContext(ClientKey, ClientApiKeyId);
        context.Request.Headers[ApiKeyHeader] = ServiceKey;

        var middleware = CreateMiddleware(_ =>
        {
            duringNext = CaptureLogContextProperties();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        duringNext.Should().NotBeNull();
        duringNext!["ClienteId"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("service");

        CaptureLogContextProperties().Should().NotContainKey("ClienteId");
    }

    [Fact]
    public async Task InvokeAsync_WithActiveClientKey_ShouldPushClienteIdAsApiKeyIdDuringNextOnly()
    {
        IReadOnlyDictionary<string, LogEventPropertyValue>? duringNext = null;
        var context = CreateHttpContext(ClientKey, ClientApiKeyId);
        context.Request.Headers[ApiKeyHeader] = ClientKey;

        var middleware = CreateMiddleware(_ =>
        {
            duringNext = CaptureLogContextProperties();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        duringNext.Should().NotBeNull();
        duringNext!["ClienteId"].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be(ClientApiKeyId.ToString());

        CaptureLogContextProperties().Should().NotContainKey("ClienteId");
    }

    private static DefaultHttpContext CreateHttpContext(string clientKey, Guid clientApiKeyId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApiKeyReadRepository>(new SingleKeyApiKeyReadRepository(clientKey, clientApiKeyId));
        services.AddSingleton<IDiagnosticContext>(new NoOpDiagnosticContext());
        services.AddSingleton<IApiKeyMetrics>(new NoOpApiKeyMetrics());

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
    }

    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate next)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKey:Key"] = ServiceKey })
            .Build();

        var authFailureLimiter = new InMemoryAuthFailureRateLimiter(
            new AuthFailureRateLimitingOptions(1000, TimeSpan.FromMinutes(1)));

        return new ApiKeyMiddleware(next, configuration, NullLogger<ApiKeyMiddleware>.Instance, authFailureLimiter);
    }

    private static IReadOnlyDictionary<string, LogEventPropertyValue> CaptureLogContextProperties()
    {
        LogEvent? captured = null;

        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("probe");

        return captured!.Properties;
    }

    private sealed class DelegatingSink(Action<LogEvent> onEmit) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => onEmit(logEvent);
    }

    private sealed class SingleKeyApiKeyReadRepository(string rawKey, Guid apiKeyId) : IApiKeyReadRepository
    {
        private readonly string _hash = ApiKeyHash.FromRawKey(rawKey).Value;

        public Task<Result<ApiKeyDto>> GetByHashAsync(string hash, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ApiKeyDto>.Failure(ApiKeyErrors.NotFound));

        public Task<Result<ApiKeyDto>> GetActiveByHashAsync(string hash, CancellationToken cancellationToken) =>
            Task.FromResult(hash == _hash
                ? Result<ApiKeyDto>.Success(new ApiKeyDto(apiKeyId, "Cliente", "clie1234", Guid.NewGuid(), true, DateTimeOffset.UtcNow, null))
                : Result<ApiKeyDto>.Failure(ApiKeyErrors.NotFound));

        public Task<Result<IReadOnlyCollection<ApiKeyDto>>> ListByDonoAsync(Guid donoUsuarioId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyCollection<ApiKeyDto>>.Success((IReadOnlyCollection<ApiKeyDto>)Array.Empty<ApiKeyDto>()));
    }

    private sealed class NoOpDiagnosticContext : IDiagnosticContext
    {
        public void Set(string propertyName, object? value, bool destructureObjects = false)
        {
        }

        public void SetException(Exception? exception)
        {
        }
    }

    private sealed class NoOpApiKeyMetrics : IApiKeyMetrics
    {
        public void RecordRequest(string cliente, string outcome)
        {
        }
    }
}
