using System.Text;
using Dapper;
using Npgsql;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class PrecoTaxaReadRepository(NpgsqlDataSource dataSource) : IPrecoTaxaReadRepository
{
    static PrecoTaxaReadRepository()
    {
        DapperTypeHandlers.Register();
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
            SELECT data_base, taxa_compra, taxa_venda, pu_compra, pu_venda, pu_base
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
                SELECT data_base, taxa_compra, taxa_venda, pu_compra, pu_venda, pu_base
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
            row.DataBase.ToString("yyyy-MM-dd"),
            row.TaxaCompra,
            row.TaxaVenda,
            row.PuCompra,
            row.PuVenda,
            row.PuBase));
    }

    public async Task<Result<IReadOnlyList<PrecoTaxaDiaDto>>> GetByDataBaseAsync(
        DateOnly dataBase,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<PrecoTaxaDiaDtoRow>(
            new CommandDefinition(
                """
                SELECT t.tipo_titulo, t.data_vencimento, p.data_base, p.taxa_compra, p.taxa_venda, p.pu_compra, p.pu_venda, p.pu_base
                FROM precos_taxas p
                JOIN titulos t ON t.id = p.titulo_id
                WHERE p.data_base = @DataBase
                """,
                new { DataBase = dataBase },
                cancellationToken: cancellationToken));

        // Codigo é derivado (não persistido): a ordem do contrato existe só aqui — o SQL não ordena de propósito.
        IReadOnlyList<PrecoTaxaDiaDto> precos = rows
            .Select(r => new PrecoTaxaDiaDto(
                TituloCodigo.From(r.TipoTitulo, r.DataVencimento),
                r.DataBase.ToString("yyyy-MM-dd"),
                r.TaxaCompra,
                r.TaxaVenda,
                r.PuCompra,
                r.PuVenda,
                r.PuBase))
            .OrderBy(dto => dto.Codigo, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<PrecoTaxaDiaDto>>.Success(precos);
    }

    private sealed record PrecoTaxaDtoRow(
        DateOnly DataBase,
        decimal? TaxaCompra,
        decimal? TaxaVenda,
        decimal? PuCompra,
        decimal? PuVenda,
        decimal? PuBase);

    private sealed record PrecoTaxaDiaDtoRow(
        string TipoTitulo,
        DateOnly DataVencimento,
        DateOnly DataBase,
        decimal? TaxaCompra,
        decimal? TaxaVenda,
        decimal? PuCompra,
        decimal? PuVenda,
        decimal? PuBase);
}
