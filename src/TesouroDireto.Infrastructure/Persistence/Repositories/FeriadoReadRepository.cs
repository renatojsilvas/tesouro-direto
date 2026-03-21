using Dapper;
using Npgsql;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class FeriadoReadRepository(NpgsqlDataSource dataSource) : IFeriadoReadRepository
{
    public async Task<Result<IReadOnlyCollection<DateOnly>>> GetAllDatasAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<DateTime>(
            new CommandDefinition(
                "SELECT data FROM feriados ORDER BY data",
                cancellationToken: cancellationToken));

        IReadOnlyCollection<DateOnly> datas = rows
            .Select(d => DateOnly.FromDateTime(d))
            .ToList();

        return Result<IReadOnlyCollection<DateOnly>>.Success(datas);
    }
}
