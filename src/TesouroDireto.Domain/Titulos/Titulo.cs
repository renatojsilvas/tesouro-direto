using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Titulos;

public sealed class Titulo : Entity<Guid>
{
    private Titulo(Guid id, TipoTitulo tipoTitulo, DataVencimento dataVencimento)
        : base(id)
    {
        TipoTitulo = tipoTitulo;
        DataVencimento = dataVencimento;
        Indexador = tipoTitulo.Indexador;
        PagaJurosSemestrais = tipoTitulo.PagaJurosSemestrais;
    }

    public TipoTitulo TipoTitulo { get; }
    public DataVencimento DataVencimento { get; }
    public Indexador Indexador { get; }
    public bool PagaJurosSemestrais { get; }

    public static Result<Titulo> Create(TipoTitulo tipoTitulo, DataVencimento dataVencimento)
    {
        ArgumentNullException.ThrowIfNull(tipoTitulo);
        ArgumentNullException.ThrowIfNull(dataVencimento);

        return new Titulo(Guid.NewGuid(), tipoTitulo, dataVencimento);
    }
}
