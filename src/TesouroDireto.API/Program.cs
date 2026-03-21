using TesouroDireto.API.Endpoints;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Middleware;
using TesouroDireto.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(TesouroDireto.Application.Importacao.ImportCsvCommand).Assembly));

var app = builder.Build();

app.UseSerilogDefaults();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGet("/", () => "Hello World!");
app.MapImportacaoEndpoints();

app.Run();

public partial class Program;
