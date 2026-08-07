using MediatR;
using TesouroDireto.API.Contracts;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Http;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.API.Endpoints;

public static class UsuarioEndpoints
{
    public static void MapUsuarioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/usuarios/sync", async (
            SyncUsuarioRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new SyncUsuarioCommand(request.GoogleSub, request.Email, request.Nome, request.EmailVerified);
            var result = await sender.Send(command, cancellationToken);

            return result.ToHttpResult(dto => Results.Ok(dto));
        })
        .AddEndpointFilter<ServiceKeyOnlyFilter>()
        .WithName("SyncUsuario")
        .WithTags("Usuarios")
        .WithSummary("Sincroniza a identidade de um usuário autenticado via Google")
        .WithDescription("Upsert idempotente: busca por google_sub, senão por email (casa o registro seed do admin), " +
            "senão cria um usuário novo não aprovado. 400 se o e-mail não foi verificado pelo provedor de identidade. " +
            "Exige a service key (chamada apenas pelo BFF).")
        .Produces<UsuarioSyncDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/admin/usuarios", async (
            bool? pendentes,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (pendentes != true)
            {
                return Result<IReadOnlyCollection<UsuarioPendenteDto>>.Failure(UsuarioErrors.FiltroPendentesObrigatorio)
                    .ToHttpResult(dtos => Results.Ok(dtos));
            }

            var result = await sender.Send(new GetUsuariosPendentesQuery(), cancellationToken);

            return result.ToHttpResult(dtos => Results.Ok(dtos));
        })
        .AddEndpointFilter<AdminOnlyFilter>()
        .WithName("GetUsuariosPendentes")
        .WithTags("Usuarios")
        .WithSummary("Lista usuários pendentes de aprovação")
        .WithDescription("Retorna usuários com aprovado=false e ativo=true, ordenados por criado_em. " +
            "Exige papel Admin e o query parameter pendentes=true; qualquer outro valor retorna 400.")
        .Produces<IReadOnlyCollection<UsuarioPendenteDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/admin/usuarios/{sub}/aprovar", async (
            string sub,
            HttpContext httpContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var admin = (Usuario)httpContext.Items[AdminOnlyFilter.AdminUsuarioItemsKey]!;
            var result = await sender.Send(new AprovarUsuarioCommand(sub, admin.Id), cancellationToken);

            return result.ToHttpResult(() => Results.NoContent());
        })
        .AddEndpointFilter<AdminOnlyFilter>()
        .WithName("AprovarUsuario")
        .WithTags("Usuarios")
        .WithSummary("Aprova um usuário pendente")
        .WithDescription("Marca aprovado=true e grava aprovado_em/aprovado_por com o id do admin autenticado. " +
            "204 em sucesso; 404 se o google_sub não existir; 403 se o requisitante não for Admin.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/admin/usuarios/{sub}/desativar", async (
            string sub,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new DesativarUsuarioCommand(sub), cancellationToken);

            return result.ToHttpResult(() => Results.NoContent());
        })
        .AddEndpointFilter<AdminOnlyFilter>()
        .WithName("DesativarUsuario")
        .WithTags("Usuarios")
        .WithSummary("Desativa um usuário")
        .WithDescription("Marca ativo=false; usuário desativado sai da listagem de pendentes. " +
            "204 em sucesso; 404 se o google_sub não existir; 403 se o requisitante não for Admin.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
