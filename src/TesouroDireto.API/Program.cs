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

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogDefaults();
app.UseHttpMetrics();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGet("/", () => "Hello World!");
app.MapImportacaoEndpoints();
app.MapTituloEndpoints();
app.MapConfiguracaoEndpoints();
app.MapSimuladorEndpoints();
app.MapMetrics();

app.Run();

public partial class Program;
