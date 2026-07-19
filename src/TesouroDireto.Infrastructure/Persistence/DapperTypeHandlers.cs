using System.Data;
using Dapper;

namespace TesouroDireto.Infrastructure.Persistence;

/// <summary>
/// Configuração global (estática/processo-wide) exigida pelos repositórios baseados em
/// Dapper deste projeto (TituloReadRepository, PrecoTaxaReadRepository):
///
/// - MatchNamesWithUnderscores é o que permite ao Dapper casar colunas snake_case
///   (data_base, taxa_compra...) com os parâmetros PascalCase dos records de leitura via
///   constructor-matching; sem isso a materialização falha por completo (nomes não
///   batem).
/// - DateOnlyTypeHandler cobre a conversão da coluna `date` do Postgres (reportada como
///   System.DateTime por Npgsql.GetFieldType) para/de DateOnly, tanto para parâmetros de
///   query quanto para o resultado.
///
/// Centralizado aqui e chamado em AddInfrastructure (uma vez, no boot, antes de
/// qualquer request) para não depender da ordem em que os cctors dos repositórios
/// individuais são tocados pela primeira vez — essa dependência implícita já causou um
/// bug real (500 em endpoints de preços quando nenhuma query em TituloReadRepository
/// rodava antes). Os repositórios também chamam Register() no próprio cctor como
/// defesa em profundidade para os testes que os instanciam diretamente, sem passar por
/// AddInfrastructure.
/// </summary>
internal static class DapperTypeHandlers
{
    private static readonly object Lock = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        lock (Lock)
        {
            if (_registered)
            {
                return;
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

            _registered = true;
        }
    }

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
