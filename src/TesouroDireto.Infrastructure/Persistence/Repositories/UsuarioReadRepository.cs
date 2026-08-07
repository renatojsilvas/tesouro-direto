using Dapper;
using Npgsql;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class UsuarioReadRepository(NpgsqlDataSource dataSource) : IUsuarioReadRepository
{
    static UsuarioReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<Result<IReadOnlyCollection<UsuarioPendenteDto>>> ListPendentesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<UsuarioPendenteDtoRow>(
            new CommandDefinition(
                """
                SELECT id, google_sub, email, nome, criado_em
                FROM usuarios
                WHERE aprovado = false AND ativo
                ORDER BY criado_em
                """,
                cancellationToken: cancellationToken));

        IReadOnlyCollection<UsuarioPendenteDto> dtos = rows.Select(r => r.ToDto()).ToList();

        return Result<IReadOnlyCollection<UsuarioPendenteDto>>.Success(dtos);
    }

    private sealed record UsuarioPendenteDtoRow(
        Guid Id,
        string GoogleSub,
        string Email,
        string Nome,
        DateTimeOffset CriadoEm)
    {
        public UsuarioPendenteDto ToDto() => new(Id, GoogleSub, Email, Nome, CriadoEm);
    }
}
