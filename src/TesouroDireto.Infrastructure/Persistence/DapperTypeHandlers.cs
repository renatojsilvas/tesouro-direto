using System.Data;
using Dapper;

namespace TesouroDireto.Infrastructure.Persistence;

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
