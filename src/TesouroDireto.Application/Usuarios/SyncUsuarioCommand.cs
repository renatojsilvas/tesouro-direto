using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed record SyncUsuarioCommand(
    string GoogleSub,
    string Email,
    string Nome,
    bool EmailVerified) : IRequest<Result<UsuarioSyncDto>>;
