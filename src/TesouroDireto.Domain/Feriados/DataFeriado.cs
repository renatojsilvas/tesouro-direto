using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Feriados;

public sealed record DataFeriado
{
    private DataFeriado(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static Result<DataFeriado> Create(DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            return FeriadoErrors.InvalidData;
        }

        return new DataFeriado(value);
    }
}
