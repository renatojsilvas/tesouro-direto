using Serilog;
using Serilog.Sinks.Grafana.Loki;
using TesouroDireto.API.Middleware;

namespace TesouroDireto.API.Extensions;

public static class SerilogExtensions
{
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        var lokiUri = builder.Configuration["Serilog:WriteTo:1:Args:uri"] ?? "http://localhost:3100";

        builder.Host.UseSerilog((context, configuration) =>
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .WriteTo.GrafanaLoki(
                    lokiUri,
                    labels: [new LokiLabel { Key = "job", Value = "tesouro-direto-api" }]));

        return builder;
    }

    public static WebApplication UseSerilogDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();

        return app;
    }
}
