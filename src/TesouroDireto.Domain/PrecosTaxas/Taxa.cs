namespace TesouroDireto.Domain.PrecosTaxas;

public sealed record Taxa
{
    private Taxa(decimal value) => Value = value;

    public decimal Value { get; }

    public static Taxa Create(decimal value) => new(value);
}
