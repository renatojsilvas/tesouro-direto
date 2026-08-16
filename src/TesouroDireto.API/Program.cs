using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using TesouroDireto.API;
using TesouroDireto.API.Endpoints;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Http;
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
app.UseMiddleware<ForwardedPrefixMiddleware>();

// UseHttpMetrics precisa envolver o UseExceptionHandler (não o contrário): o prometheus-net
// só lê o status code final da resposta no "finally" do seu próprio middleware, e é o
// UseExceptionHandler quem reescreve a resposta para 5xx quando um endpoint lança exceção.
// Se o UseHttpMetrics ficar por dentro (mais perto do endpoint), a exceção atravessa o seu
// try/finally antes do status ser reescrito, e o label `code` fica errado — mascarando
// incidentes no alerta td-http-5xx-alto. Ver docs/PLANO.md (tarefa 29) e
// tests/.../HttpMetricsExceptionOrderingTests.cs.
var httpMetricsExcludedPaths = app.Configuration.GetSection("ApiKey:ExcludedPaths").Get<string[]>() ?? [];
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());

app.UseExceptionHandler();

app.UseSerilogDefaults();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/", () => "Hello World!").ExcludeFromDescription();
var v1 = app.MapGroup("/v1");
v1.MapImportacaoEndpoints();
v1.MapTituloEndpoints();
v1.MapPrecoEndpoints();
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
