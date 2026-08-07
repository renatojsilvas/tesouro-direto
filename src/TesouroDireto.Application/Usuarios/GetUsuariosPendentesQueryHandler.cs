using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed class GetUsuariosPendentesQueryHandler(IUsuarioReadRepository usuarioReadRepository)
    : IRequestHandler<GetUsuariosPendentesQuery, Result<IReadOnlyCollection<UsuarioPendenteDto>>>
{
    public Task<Result<IReadOnlyCollection<UsuarioPendenteDto>>> Handle(GetUsuariosPendentesQuery request, CancellationToken cancellationToken) =>
        usuarioReadRepository.ListPendentesAsync(cancellationToken);
}
