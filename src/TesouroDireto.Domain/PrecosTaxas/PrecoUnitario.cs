using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public sealed record PrecoUnitario
{
    private PrecoUnitario(decimal value) => Value = value;

    public decimal Value { get; }

    public static Result<PrecoUnitario> Create(decimal value)
    {
        if (value <= 0)
        {
            return new Error("PrecoUnitario.Invalid", "Preco unitario must be greater than zero.");
        }

        return new PrecoUnitario(value);
    }
}
