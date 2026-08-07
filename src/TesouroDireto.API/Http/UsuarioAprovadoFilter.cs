using Microsoft.AspNetCore.Mvc;
using TesouroDireto.Application.Usuarios;

namespace TesouroDireto.API.Http;

public sealed class UsuarioAprovadoFilter : IEndpointFilter
{
    public const string UsuarioAprovadoItemsKey = "UsuarioAprovado";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.IsServiceIdentity())
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        if (!httpContext.Request.Headers.TryGetValue(AdminOnlyFilter.ActingUserSubHeader, out var subHeader)
            || string.IsNullOrWhiteSpace(subHeader))
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        var repository = httpContext.RequestServices.GetRequiredService<IUsuarioWriteRepository>();
        var result = await repository.GetByGoogleSubAsync(subHeader.ToString(), httpContext.RequestAborted);

        if (result.IsFailure || !result.Value.Aprovado || !result.Value.Ativo)
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        httpContext.Items[UsuarioAprovadoItemsKey] = result.Value;

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
