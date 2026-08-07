using Microsoft.AspNetCore.Mvc;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.API.Http;

public sealed class AdminOnlyFilter : IEndpointFilter
{
    public const string ActingUserSubHeader = "X-Acting-User-Sub";
    public const string AdminUsuarioItemsKey = "AdminUsuario";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue(ActingUserSubHeader, out var subHeader)
            || string.IsNullOrWhiteSpace(subHeader))
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        var repository = httpContext.RequestServices.GetRequiredService<IUsuarioWriteRepository>();
        var result = await repository.GetByGoogleSubAsync(subHeader.ToString(), httpContext.RequestAborted);

        if (result.IsFailure || result.Value.Papel != PapelUsuario.Admin || !result.Value.Aprovado || !result.Value.Ativo)
        {
            await WriteForbiddenAsync(httpContext);
            return Results.Empty;
        }

        httpContext.Items[AdminUsuarioItemsKey] = result.Value;

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
