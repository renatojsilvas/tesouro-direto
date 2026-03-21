using System.Data;
using System.Reflection;
using System.Text;
using Dapper;
using Npgsql;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TituloReadRepository(NpgsqlDataSource dataSource) : ITituloReadRepository
{
    private static readonly ConstructorInfo TituloConstructor = typeof(Titulo)
        .GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(Guid), typeof(TipoTitulo), typeof(DataVencimento)])!;

    static TituloReadRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }

    public async Task<IReadOnlyCollection<Titulo>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<TituloRow>(
            new CommandDefinition(
                "SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais FROM titulos",
                cancellationToken: cancellationToken));

        return rows.Select(MapToEntity).ToList();
    }

    public async Task<Titulo?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<TituloRow>(
            new CommandDefinition(
                "SELECT id, tipo_titulo, data_vencimento, indexador, paga_juros_semestrais FROM titulos WHERE id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));

        return row is null ? null : MapToEntity(row);
    }

    public async Task<IReadOnlyCollection<TituloDto>> GetFilteredAsync(
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

        return rows.Select(r => new TituloDto(
            r.Id,
            r.TipoTitulo,
            r.DataVencimento.ToString("yyyy-MM-dd"),
            r.Indexador,
            r.PagaJurosSemestrais,
            r.Vencido)).ToList();
    }

    private static Titulo MapToEntity(TituloRow row)
    {
        var tipo = TipoTitulo.FromName(row.TipoTitulo).Value;
        var vencimento = DataVencimento.Create(row.DataVencimento).Value;

        return (Titulo)TituloConstructor.Invoke([row.Id, tipo, vencimento]);
    }

    private sealed record TituloRow(
        Guid Id,
        string TipoTitulo,
        DateOnly DataVencimento,
        string Indexador,
        bool PagaJurosSemestrais);

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
