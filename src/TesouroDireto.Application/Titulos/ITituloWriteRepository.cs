using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Titulos;

public interface ITituloWriteRepository
{
    Task<Result> AddAsync(Titulo titulo, CancellationToken cancellationToken);
    Task<Result<bool>> ExistsAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken);
    Task<Result<Titulo>> GetByTipoAndVencimentoAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken);
}
