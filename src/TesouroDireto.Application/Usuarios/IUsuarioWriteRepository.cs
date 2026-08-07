using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Usuarios;

public interface IUsuarioWriteRepository
{
    Task<Result> AddAsync(Usuario usuario, CancellationToken cancellationToken);
    Task<Result<Usuario>> AddOrGetExistingAsync(Usuario usuario, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(Usuario usuario, CancellationToken cancellationToken);
    Task<Result<Usuario>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<Usuario>> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken);
    Task<Result<Usuario>> GetByEmailAsync(Email email, CancellationToken cancellationToken);
}
