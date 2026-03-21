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

        app.MapPut("/configuracoes/tributos/{id:guid}", async (
            Guid id,
            UpdateTributoRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTributoCommand(id, request.Ativo, request.Faixas);
            var result = await sender.Send(command, cancellationToken);

            if (result.IsSuccess)
            {
                return Results.NoContent();
            }

            return result.Error.Code.Contains("NotFound", StringComparison.Ordinal)
                ? Results.NotFound(new { result.Error.Code, result.Error.Description })
                : Results.BadRequest(new { result.Error.Code, result.Error.Description });
        });
    }

    public sealed record UpdateTributoRequest(bool Ativo, IReadOnlyCollection<FaixaDto> Faixas);
}
