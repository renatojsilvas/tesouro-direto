using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public interface IUsuarioReadRepository
{
    Task<Result<IReadOnlyCollection<UsuarioPendenteDto>>> ListPendentesAsync(CancellationToken cancellationToken);
}
