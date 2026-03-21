using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.PrecosTaxas;

public sealed class PrecoTaxa : Entity<Guid>
{
    private PrecoTaxa(
        Guid id,
        Guid tituloId,
        DataBase dataBase,
        Taxa? taxaCompra,
        Taxa? taxaVenda,
        PrecoUnitario? puCompra,
        PrecoUnitario? puVenda,
        PrecoUnitario? puBase)
        : base(id)
    {
        TituloId = tituloId;
        DataBase = dataBase;
        TaxaCompra = taxaCompra;
        TaxaVenda = taxaVenda;
        PuCompra = puCompra;
        PuVenda = puVenda;
        PuBase = puBase;
    }

    public Guid TituloId { get; }
    public DataBase DataBase { get; }
    public Taxa? TaxaCompra { get; }
    public Taxa? TaxaVenda { get; }
    public PrecoUnitario? PuCompra { get; }
    public PrecoUnitario? PuVenda { get; }
    public PrecoUnitario? PuBase { get; }

    public static Result<PrecoTaxa> Create(
        Guid tituloId,
        DataBase dataBase,
        Taxa? taxaCompra,
        Taxa? taxaVenda,
        PrecoUnitario? puCompra,
        PrecoUnitario? puVenda,
        PrecoUnitario? puBase)
    {
        if (tituloId == Guid.Empty)
        {
            return PrecoTaxaErrors.InvalidTituloId;
        }

        ArgumentNullException.ThrowIfNull(dataBase);

        return new PrecoTaxa(Guid.NewGuid(), tituloId, dataBase, taxaCompra, taxaVenda, puCompra, puVenda, puBase);
    }
}
