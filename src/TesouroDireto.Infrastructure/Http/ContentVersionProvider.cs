using System.Globalization;
using Dapper;
using Npgsql;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.Infrastructure.Http;

public sealed class ContentVersionProvider(NpgsqlDataSource dataSource) : IContentVersionProvider
{
    static ContentVersionProvider()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<ContentVersionRow>(
            new CommandDefinition(
                """
                SELECT
                    (SELECT max(data_base) FROM precos_taxas) AS MaxDataBase,
                    (SELECT count(*) FROM precos_taxas) AS PrecosCount,
                    (SELECT count(*) FROM titulos) AS TitulosCount
                """,
                cancellationToken: cancellationToken));

        var maxDataBase = row.MaxDataBase?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";

        return $"{maxDataBase}-{row.PrecosCount}-{row.TitulosCount}";
    }

    private sealed record ContentVersionRow(DateOnly? MaxDataBase, long PrecosCount, long TitulosCount);
}
