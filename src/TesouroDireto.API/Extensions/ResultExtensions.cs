using TesouroDireto.Domain.Common;

namespace TesouroDireto.API.Extensions;

/// <summary>
/// Converte <see cref="Result"/>/<see cref="Result{T}"/> em <see cref="IResult"/>,
/// mapeando falhas para problem+json de forma consistente com o
/// <c>CustomizeProblemDetails</c> configurado em Program.cs (correlationId + traceId).
/// A escrita usa <see cref="IProblemDetailsService"/>.WriteAsync explicitamente
/// (em vez de <see cref="Results.Problem"/>) para deixar claro, no ponto de uso,
/// que a resposta passa pelo pipeline de ProblemDetails do ASP.NET Core.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess()
            : ToProblemResult(result.Error);
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value)
            : ToProblemResult(result.Error);
    }

    private static IResult ToProblemResult(Error error)
    {
        var status = error.Code.EndsWith(".NotFound", StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        var title = status switch
        {
            StatusCodes.Status404NotFound => "Recurso não encontrado",
            _ => "Requisição inválida",
        };

        return new ResultProblemHttpResult(status, title, error);
    }

    private sealed class ResultProblemHttpResult(int status, string title, Error error) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

            var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = error.Description,
            };
            problemDetails.Extensions["code"] = error.Code;

            httpContext.Response.StatusCode = status;
            problemDetails.Status = status;

            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
            });
        }
    }
}
