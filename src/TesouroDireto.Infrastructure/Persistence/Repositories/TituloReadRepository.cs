using System.Text;
using Dapper;
using Npgsql;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TituloReadRepository(NpgsqlDataSource dataSource) : ITituloReadRepository
{
    static TituloReadRepository()
    {
        DapperTypeHandlers.Register();
    }

    public async Task<Result<IReadOnlyCollection<TituloDto>>> GetFilteredAsync(
        string? indexador,
        bool? vencido,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var sql = new StringBuilder(
            """
            SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais,
                   CASE WHEN data_vencimento < @Today THEN true ELSE false END AS vencido
            FROM titulos
            WHERE 1 = 1
            """);

        var parameters = new DynamicParameters();
        parameters.Add("Today", DateOnly.FromDateTime(DateTime.UtcNow));

        if (indexador is not null)
        {
            sql.Append(" AND indexador = @Indexador");
            parameters.Add("Indexador", indexador);
        }

        if (vencido is not null)
        {
            sql.Append(vencido.Value
                ? " AND data_vencimento < @Today"
                : " AND data_vencimento >= @Today");
        }

        sql.Append(" ORDER BY tipo_titulo, data_vencimento");

        var rows = await connection.QueryAsync<TituloDtoRow>(
            new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken));

        IReadOnlyCollection<TituloDto> titulos = rows.Select(r => new TituloDto(
            r.TipoTitulo,
            r.DataVencimento.ToString("yyyy-MM-dd"),
            r.Indexador,
            r.PagaJurosSemestrais,
            r.Vencido,
            TituloCodigo.From(r.TipoTitulo, r.DataVencimento))).ToList();

        return Result<IReadOnlyCollection<TituloDto>>.Success(titulos);
    }

    public async Task<Result<Guid>> GetIdByNomeAsync(string nome, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<TituloDtoRow>(
            new CommandDefinition(
                """
                SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais,
                       CASE WHEN data_vencimento < @Today THEN true ELSE false END AS vencido
                FROM titulos
                WHERE UPPER(tipo_titulo || ' ' || EXTRACT(YEAR FROM data_vencimento)::text) = UPPER(@Nome)
                """,
                new { Nome = nome.Trim(), Today = DateOnly.FromDateTime(DateTime.UtcNow) },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return TituloErrors.NotFound;
        }

        return Result<Guid>.Success(row.Id);
    }

    public async Task<Result<Guid>> GetIdByCodigoAsync(string codigo, CancellationToken cancellationToken)
    {
        if (!TituloCodigo.TryParseDate(codigo, out var data))
        {
            return TituloErrors.InvalidCodigo;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<TituloDtoRow>(
            new CommandDefinition(
                """
                SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais,
                       CASE WHEN data_vencimento < @Today THEN true ELSE false END AS vencido
                FROM titulos
                WHERE data_vencimento = @Data
                """,
                new { Data = data, Today = DateOnly.FromDateTime(DateTime.UtcNow) },
                cancellationToken: cancellationToken));

        foreach (var row in rows)
        {
            if (string.Equals(TituloCodigo.From(row.TipoTitulo, row.DataVencimento), codigo, StringComparison.Ordinal))
            {
                return Result<Guid>.Success(row.Id);
            }
        }

        return TituloErrors.NotFound;
    }

    private sealed record TituloDtoRow(
        Guid Id,
        string TipoTitulo,
        DateOnly DataVencimento,
        string Indexador,
        bool PagaJurosSemestrais,
        bool Vencido);
}
