using Dapper;
using Npgsql;
using TesouroDireto.Application.ApiKeys;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class ApiKeyReadRepository(NpgsqlDataSource dataSource) : IApiKeyReadRepository
{
    static ApiKeyReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<Result<ApiKeyDto>> GetByHashAsync(string hash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<ApiKeyDtoRow>(
            new CommandDefinition(
                """
                SELECT id, nome, prefixo, dono_usuario_id, ativa, criada_em, ultimo_uso_em
                FROM api_keys
                WHERE hash = @Hash
                """,
                new { Hash = hash },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return Result<ApiKeyDto>.Failure(ApiKeyErrors.NotFound);
        }

        return Result<ApiKeyDto>.Success(row.ToDto());
    }

    public async Task<Result<IReadOnlyCollection<ApiKeyDto>>> ListByDonoAsync(Guid donoUsuarioId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<ApiKeyDtoRow>(
            new CommandDefinition(
                """
                SELECT id, nome, prefixo, dono_usuario_id, ativa, criada_em, ultimo_uso_em
                FROM api_keys
                WHERE dono_usuario_id = @DonoUsuarioId
                ORDER BY criada_em DESC
                """,
                new { DonoUsuarioId = donoUsuarioId },
                cancellationToken: cancellationToken));

        IReadOnlyCollection<ApiKeyDto> apiKeys = rows.Select(r => r.ToDto()).ToList();

        return Result<IReadOnlyCollection<ApiKeyDto>>.Success(apiKeys);
    }

    private sealed record ApiKeyDtoRow(
        Guid Id,
        string Nome,
        string Prefixo,
        Guid DonoUsuarioId,
        bool Ativa,
        DateTimeOffset CriadaEm,
        DateTimeOffset? UltimoUsoEm)
    {
        public ApiKeyDto ToDto() => new(Id, Nome, Prefixo, DonoUsuarioId, Ativa, CriadaEm, UltimoUsoEm);
    }
}
