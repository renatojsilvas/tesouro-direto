using Microsoft.EntityFrameworkCore;
using TesouroDireto.Application.Titulos;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class TituloWriteRepository(AppDbContext dbContext) : ITituloWriteRepository
{
    public async Task<Result> AddAsync(Titulo titulo, CancellationToken cancellationToken)
    {
        await dbContext.Titulos.AddAsync(titulo, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<bool>> ExistsAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Titulos
            .AnyAsync(t => t.TipoTitulo == tipoTitulo && t.DataVencimento == dataVencimento, cancellationToken);

        return Result<bool>.Success(exists);
    }

    public async Task<Result<Titulo?>> GetByTipoAndVencimentoAsync(TipoTitulo tipoTitulo, DataVencimento dataVencimento, CancellationToken cancellationToken)
    {
        var titulo = await dbContext.Titulos
            .FirstOrDefaultAsync(t => t.TipoTitulo == tipoTitulo && t.DataVencimento == dataVencimento, cancellationToken);

        return Result<Titulo?>.Success(titulo);
    }
}
