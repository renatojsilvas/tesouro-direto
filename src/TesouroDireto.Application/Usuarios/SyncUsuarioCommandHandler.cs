using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Usuarios;

public sealed class SyncUsuarioCommandHandler(
    IUsuarioWriteRepository usuarioWriteRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<SyncUsuarioCommand, Result<UsuarioSyncDto>>
{
    public async Task<Result<UsuarioSyncDto>> Handle(SyncUsuarioCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleSub))
        {
            return UsuarioErrors.GoogleSubObrigatorio;
        }

        if (string.IsNullOrWhiteSpace(request.Nome))
        {
            return UsuarioErrors.InvalidNome;
        }

        if (!request.EmailVerified)
        {
            return UsuarioErrors.EmailNaoVerificado;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var porSub = await usuarioWriteRepository.GetByGoogleSubAsync(request.GoogleSub, cancellationToken);
        if (porSub.IsSuccess)
        {
            return ToDto(porSub.Value);
        }

        var porEmail = await usuarioWriteRepository.GetByEmailAsync(emailResult.Value, cancellationToken);
        if (porEmail.IsSuccess)
        {
            var usuarioExistente = porEmail.Value;
            var definir = usuarioExistente.DefinirGoogleSub(request.GoogleSub);
            if (definir.IsFailure)
            {
                return definir.Error;
            }

            var update = await usuarioWriteRepository.UpdateAsync(usuarioExistente, cancellationToken);
            if (update.IsFailure)
            {
                return update.Error;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(usuarioExistente);
        }

        var criacao = Usuario.Create(emailResult.Value, request.Nome, PapelUsuario.User, timeProvider.GetUtcNow(), request.GoogleSub);
        if (criacao.IsFailure)
        {
            return criacao.Error;
        }

        var adicionado = await usuarioWriteRepository.AddOrGetExistingAsync(criacao.Value, cancellationToken);
        if (adicionado.IsFailure)
        {
            return adicionado.Error;
        }

        return ToDto(adicionado.Value);
    }

    private static UsuarioSyncDto ToDto(Usuario usuario) => new(usuario.Id, usuario.Aprovado);
}
