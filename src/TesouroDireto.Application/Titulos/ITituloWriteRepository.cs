using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public interface ITituloWriteRepository
{
    Task AddAsync(Titulo titulo, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken);
    Task<Titulo?> GetByTipoAndVencimentoAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken);
}
