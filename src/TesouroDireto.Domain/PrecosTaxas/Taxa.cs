using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public sealed record Taxa
{
    private Taxa(decimal value) => Value = value;

    public decimal Value { get; }

    public static Result<Taxa> Create(decimal value)
    {
        if (value < 0)
        {
            return new Error("Taxa.Invalid", "Taxa must not be negative.");
        }

        return new Taxa(value);
    }
}
