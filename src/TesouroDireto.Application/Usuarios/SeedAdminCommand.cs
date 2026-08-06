using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Usuarios;

public sealed record SeedAdminCommand : IRequest<Result>;
