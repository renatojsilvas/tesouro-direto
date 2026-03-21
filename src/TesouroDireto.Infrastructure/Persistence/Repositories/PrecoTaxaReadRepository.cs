using System.Text;
using Dapper;
using Npgsql;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class PrecoTaxaReadRepository(NpgsqlDataSource dataSource) : IPrecoTaxaReadRepository
{
    public async Task<Result<bool>> TituloExistsAsync(Guid tituloId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM titulos WHERE id = @TituloId)",
                new { TituloId = tituloId },
                cancellationToken: cancellationToken));

        return Result<bool>.Success(exists);
    }

    public async Task<Result<IReadOnlyCollection<PrecoTaxaDto>>> GetByTituloIdAsync(
        Guid tituloId,
        DateOnly? dataInicio,
        DateOnly? dataFim,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var sql = new StringBuilder(
            """
            SELECT id, data_base, taxa_compra, taxa_venda, pu_compra, pu_venda, pu_base
            FROM precos_taxas
            WHERE titulo_id = @TituloId
            """);

        var parameters = new DynamicParameters();
        parameters.Add("TituloId", tituloId);

        if (dataInicio is not null)
        {
            sql.Append(" AND data_base >= @DataInicio");
            parameters.Add("DataInicio", dataInicio.Value);
        }

        if (dataFim is not null)
        {
            sql.Append(" AND data_base <= @DataFim");
            parameters.Add("DataFim", dataFim.Value);
        }

        sql.Append(" ORDER BY data_base ASC");

        var rows = await connection.QueryAsync<PrecoTaxaDtoRow>(
            new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken));

        IReadOnlyCollection<PrecoTaxaDto> precos = rows.Select(r => new PrecoTaxaDto(
            r.Id,
            r.DataBase.ToString("yyyy-MM-dd"),
            r.TaxaCompra,
            r.TaxaVenda,
            r.PuCompra,
            r.PuVenda,
            r.PuBase)).ToList();

        return Result<IReadOnlyCollection<PrecoTaxaDto>>.Success(precos);
    }

    public async Task<Result<PrecoTaxaDto>> GetLatestByTituloIdAsync(Guid tituloId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<PrecoTaxaDtoRow>(
            new CommandDefinition(
                """
                SELECT id, data_base, taxa_compra, taxa_venda, pu_compra, pu_venda, pu_base
                FROM precos_taxas
                WHERE titulo_id = @TituloId
                ORDER BY data_base DESC
                LIMIT 1
                """,
                new { TituloId = tituloId },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return Domain.PrecosTaxas.PrecoTaxaErrors.NotFound;
        }

        return Result<PrecoTaxaDto>.Success(new PrecoTaxaDto(
            row.Id,
            row.DataBase.ToString("yyyy-MM-dd"),
            row.TaxaCompra,
            row.TaxaVenda,
            row.PuCompra,
            row.PuVenda,
            row.PuBase));
    }

    private sealed record PrecoTaxaDtoRow(
        Guid Id,
        DateOnly DataBase,
        decimal? TaxaCompra,
        decimal? TaxaVenda,
        decimal? PuCompra,
        decimal? PuVenda,
        decimal? PuBase);
}
