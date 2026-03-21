using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TituloWriteRepository(AppDbContext dbContext) : ITituloWriteRepository
{
    public async Task AddAsync(Titulo titulo, CancellationToken cancellationToken)
    {
        await dbContext.Titulos.AddAsync(titulo, cancellationToken);
    }

    public async Task<bool> ExistsAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken)
    {
        return await dbContext.Titulos
            .AnyAsync(t => t.TipoTitulo == tipoTitulo && t.DataVencimento == dataVencimento, cancellationToken);
    }

    public async Task<Titulo?> GetByTipoAndVencimentoAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken)
    {
        return await dbContext.Titulos
            .FirstOrDefaultAsync(t => t.TipoTitulo == tipoTitulo && t.DataVencimento == dataVencimento, cancellationToken);
    }
}
