using TesouroDireto.API.Extensions;
using TesouroDireto.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();

var app = builder.Build();

app.UseSerilogDefaults();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGet("/", () => "Hello World!");

app.Run();

public partial class Program;
