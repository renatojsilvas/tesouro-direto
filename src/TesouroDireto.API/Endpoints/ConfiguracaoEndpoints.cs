using MediatR;
using TesouroDireto.API.Contracts;
using TesouroDireto.API.Extensions;
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

            return result.ToHttpResult(dtos => Results.Ok(dtos));
        });

        app.MapPut("/configuracoes/tributos/{id:guid}", async (
            Guid id,
            UpdateTributoRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateTributoCommand(id, request.Ativo, request.Faixas);
            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(() => Results.NoContent());
        });

        app.MapPost("/configuracoes/tributos", async (
            CreateTributoRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTributoCommand(
                request.Nome,
                request.BaseCalculo,
                request.TipoCalculo,
                request.Faixas,
                request.Ordem,
                request.Cumulativo);
            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(id => Results.Created($"/configuracoes/tributos/{id}", new { Id = id }));
        });
    }

    public sealed record UpdateTributoRequest(bool Ativo, IReadOnlyCollection<FaixaDto> Faixas);
}
