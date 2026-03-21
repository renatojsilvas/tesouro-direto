using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public sealed record DataVencimento
{
    private DataVencimento(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static Result<DataVencimento> Create(DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            return new Error("DataVencimento.Invalid", "Data de vencimento must not be empty.");
        }

        return new DataVencimento(value);
    }
}
