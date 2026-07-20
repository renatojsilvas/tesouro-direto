using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using TesouroDireto.API.Endpoints;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Middleware;
using TesouroDireto.Infrastructure;
using TesouroDireto.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(TesouroDireto.Application.Importacao.ImportCsvCommand).Assembly));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items["CorrelationId"] is string correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }

        // ProblemDetails não injeta traceId por padrão fora do contexto MVC/[ApiController];
        // seguimos o padrão oficial da Microsoft para minimal APIs (CustomizeProblemDetails)
        // usando o Activity atual (W3C trace id) com fallback para o TraceIdentifier do ASP.NET Core.
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

var app = builder.Build();

ApiKeyGuard.Validate(app.Configuration, app.Environment);

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionHandler();

app.UseSerilogDefaults();
app.UseHttpMetrics();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/", () => "Hello World!");
app.MapImportacaoEndpoints();
app.MapTituloEndpoints();
app.MapConfiguracaoEndpoints();
app.MapSimuladorEndpoints();
app.MapMetrics();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/_test/throw", IResult () => throw new InvalidOperationException("Forced exception for exception handler testing."));
}

app.Run();

public partial class Program;
