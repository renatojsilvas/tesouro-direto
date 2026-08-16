using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Prometheus;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.OpenApi;
using TesouroDireto.Infrastructure.Persistence;
using TesouroDireto.Infrastructure.RateLimiting;

namespace TesouroDireto.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>().ForwardToPrometheus();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator>();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
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

            c.SchemaFilter<EnumSchemaFilter>();
            c.OperationFilter<PrecosPaginacaoOperationFilter>();
            c.OperationFilter<PrecosPorDataOperationFilter>();
            c.OperationFilter<TributoLocationOperationFilter>();
        });

        services.AddProblemDetails(options =>
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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var store = context.RequestServices.GetRequiredService<IRateLimitStore>();
                var clienteId = context.Items.TryGetValue(RateLimitIdentity.ClienteIdItemsKey, out var value)
                    ? value as string
                    : null;
                return store.GetPartition(clienteId);
            });
            options.OnRejected = async (rejected, token) =>
            {
                var httpContext = rejected.HttpContext;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    httpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var clienteId = httpContext.Items.TryGetValue(RateLimitIdentity.ClienteIdItemsKey, out var value)
                    ? value as string ?? "unknown"
                    : "unknown";
                var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("TesouroDireto.API.RateLimiting");
                logger.LogWarning("Rate limit exceeded for cliente {ClienteId} on {Path}", clienteId, httpContext.Request.Path);
                var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests"
                    }
                });
            };
        });

        return services;
    }
}
