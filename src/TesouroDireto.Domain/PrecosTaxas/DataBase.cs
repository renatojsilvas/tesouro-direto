using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public sealed record DataBase
{
    private DataBase(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static Result<DataBase> Create(DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            return new Error("DataBase.Invalid", "Data base must not be empty.");
        }

        return new DataBase(value);
    }
}
