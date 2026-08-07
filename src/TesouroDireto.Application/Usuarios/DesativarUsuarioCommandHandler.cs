using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed class DesativarUsuarioCommandHandler(
    IUsuarioWriteRepository usuarioWriteRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DesativarUsuarioCommand, Result>
{
    public async Task<Result> Handle(DesativarUsuarioCommand request, CancellationToken cancellationToken)
    {
        var usuarioResult = await usuarioWriteRepository.GetByGoogleSubAsync(request.Sub, cancellationToken);
        if (usuarioResult.IsFailure)
        {
            return Result.Failure(usuarioResult.Error);
        }

        var usuario = usuarioResult.Value;
        usuario.Desativar();

        var updateResult = await usuarioWriteRepository.UpdateAsync(usuario, cancellationToken);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
