using MediatR;
using TesouroDireto.Application.Tributos;

namespace TesouroDireto.API.Endpoints;

public static class ConfiguracaoEndpoints
{
    public static void MapConfiguracaoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/configuracoes/tributos", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTributosQuery(), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });
    }
}
