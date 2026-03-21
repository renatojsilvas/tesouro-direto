using System.Data;
using System.Text;
using Dapper;
using Npgsql;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TituloReadRepository(NpgsqlDataSource dataSource) : ITituloReadRepository
{
    static TituloReadRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
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
            r.Id,
            r.TipoTitulo,
            r.DataVencimento.ToString("yyyy-MM-dd"),
            r.Indexador,
            r.PagaJurosSemestrais,
            r.Vencido)).ToList();

        return Result<IReadOnlyCollection<TituloDto>>.Success(titulos);
    }

    private sealed record TituloDtoRow(
        Guid Id,
        string TipoTitulo,
        DateOnly DataVencimento,
        string Indexador,
        bool PagaJurosSemestrais,
        bool Vencido);

    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }
}
