using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Feriados;

public sealed class Feriado : Entity<Guid>
{
    private Feriado(Guid id, DataFeriado data, string descricao)
        : base(id)
    {
        Data = data;
        Descricao = descricao;
    }

    public DataFeriado Data { get; }
    public string Descricao { get; }

    public static Result<Feriado> Create(DataFeriado data, string descricao)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(descricao))
        {
            return FeriadoErrors.InvalidDescricao;
        }

        return new Feriado(Guid.NewGuid(), data, descricao.Trim());
    }
}
