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

    // Ver EnumSchemaFilter: Swashbuckle não lê o JsonStringEnumConverter registrado via
    // ConfigureHttpJsonOptions (Minimal API), então os enums sairiam como integer sem isto.
    c.SchemaFilter<TesouroDireto.API.OpenApi.EnumSchemaFilter>();
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

await app.InitializeDatabaseAsync();

app.UseExceptionHandler();

app.UseSerilogDefaults();

// Tarefa 33: /swagger fica ANTES do ApiKeyMiddleware só em Development (UI acessível sem
// key para uso local) e DEPOIS dele nos demais ambientes (swagger.json exige X-Api-Key,
// coerente com o resto das rotas de negócio; a UI não é montada fora de Development).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Reusa a lista ApiKey:ExcludedPaths (paths de infra: /health*, /metrics) para excluir
// esses paths da instrumentação server-side do prometheus-net, evitando que eles inflem
// o denominador da regra de alerta td-http-5xx-alto. "/swagger" entra só aqui (lista local),
// nunca em ApiKey:ExcludedPaths — o swagger.json continua atrás da key em produção.
var httpMetricsExcludedPaths = (app.Configuration.GetSection("ApiKey:ExcludedPaths").Get<string[]>() ?? [])
    .Append("/swagger")
    .ToArray();
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());

app.UseMiddleware<ApiKeyMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseSwagger();
}

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
