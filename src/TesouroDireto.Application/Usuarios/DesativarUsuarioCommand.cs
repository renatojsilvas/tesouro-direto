using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed record DesativarUsuarioCommand(string Sub) : IRequest<Result>;
