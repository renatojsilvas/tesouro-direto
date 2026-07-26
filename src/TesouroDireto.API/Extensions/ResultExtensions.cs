using TesouroDireto.Domain.Common;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace TesouroDireto.API.Extensions;

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
