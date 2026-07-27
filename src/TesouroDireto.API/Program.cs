using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
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
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>().ForwardToPrometheus();
builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Tesouro Direto API", Version = "v1" });

    var apiKeyScheme = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "Chave de API obrigatória em todas as rotas de negócio.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
    };
    c.AddSecurityDefinition("ApiKey", apiKeyScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [apiKeyScheme] = Array.Empty<string>(),
    });

    c.SchemaFilter<TesouroDireto.API.OpenApi.EnumSchemaFilter>();
    c.OperationFilter<TesouroDireto.API.OpenApi.PrecosPaginacaoOperationFilter>();
    c.OperationFilter<TesouroDireto.API.OpenApi.TributoLocationOperationFilter>();
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items["CorrelationId"] is string correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }

        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

var app = builder.Build();

ApiKeyGuard.Validate(app.Configuration, app.Environment);

await app.InitializeDatabaseAsync();

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

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/", () => "Hello World!").ExcludeFromDescription();
app.MapImportacaoEndpoints();
app.MapTituloEndpoints();
app.MapConfiguracaoEndpoints();
app.MapSimuladorEndpoints();
app.MapMetrics();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/_test/throw", IResult () => throw new InvalidOperationException("Forced exception for exception handler testing."))
        .ExcludeFromDescription();
}

app.Run();

public partial class Program;
