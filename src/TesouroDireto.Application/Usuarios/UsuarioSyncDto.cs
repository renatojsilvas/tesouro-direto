using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Usuarios;

public sealed record UsuarioSyncDto(Guid Id, bool Aprovado, PapelUsuario Papel);
