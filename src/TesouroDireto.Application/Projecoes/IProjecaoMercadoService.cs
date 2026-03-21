using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Application.Projecoes;

public interface IProjecaoMercadoService
{
    Task<Result<ProjecaoMercado>> GetProjecaoAsync(Indexador indexador, CancellationToken cancellationToken);
}
