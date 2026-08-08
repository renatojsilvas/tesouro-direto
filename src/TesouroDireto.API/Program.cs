using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using TesouroDireto.API;
using TesouroDireto.API.Endpoints;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Middleware;
using TesouroDireto.Application;
using TesouroDireto.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

ApiKeyGuard.Validate(app.Configuration, app.Environment);

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseSerilogDefaults();

app.UseSwagger();
app.UseSwaggerUI();

var httpMetricsExcludedPaths = app.Configuration.GetSection("ApiKey:ExcludedPaths").Get<string[]>() ?? [];
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());

app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/", () => "Hello World!").ExcludeFromDescription();
var v1 = app.MapGroup("/v1");
v1.MapImportacaoEndpoints();
v1.MapTituloEndpoints();
v1.MapConfiguracaoEndpoints();
v1.MapUsuarioEndpoints();
v1.MapApiKeyEndpoints();
v1.MapSimuladorEndpoints();
app.MapMetrics();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/_test/throw", IResult () => throw new InvalidOperationException("Forced exception for exception handler testing."))
        .ExcludeFromDescription();
}

app.Run();

public partial class Program;
