using TesouroDireto.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();

var app = builder.Build();

app.UseSerilogDefaults();

app.MapGet("/", () => "Hello World!");

app.Run();

public partial class Program;
