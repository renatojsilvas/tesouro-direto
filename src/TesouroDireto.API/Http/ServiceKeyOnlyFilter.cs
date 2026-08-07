using Microsoft.AspNetCore.Mvc;

namespace TesouroDireto.API.Http;

public sealed class ServiceKeyOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.IsServiceIdentity())
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        return await next(context);
    }

    private static async Task WriteForbiddenAsync(HttpContext httpContext)
    {
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden" }
        });
    }
}
