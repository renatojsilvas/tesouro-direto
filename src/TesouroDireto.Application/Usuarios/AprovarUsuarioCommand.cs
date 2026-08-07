using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed record AprovarUsuarioCommand(string Sub, Guid AdminId) : IRequest<Result>;
