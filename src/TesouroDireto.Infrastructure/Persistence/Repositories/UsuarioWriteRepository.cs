using Microsoft.EntityFrameworkCore;
using Npgsql;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Infrastructure.Persistence.Repositories;

public sealed class UsuarioWriteRepository(AppDbContext dbContext) : IUsuarioWriteRepository
{
    public async Task<Result> AddAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        var jaExiste = await dbContext.Usuarios
            .AnyAsync(u => u.Email == usuario.Email, cancellationToken);

        if (jaExiste)
        {
            return Result.Failure(UsuarioErrors.AlreadyExists);
        }

        await dbContext.Usuarios.AddAsync(usuario, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<Usuario>> AddOrGetExistingAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        await dbContext.Usuarios.AddAsync(usuario, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<Usuario>.Success(usuario);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            dbContext.Entry(usuario).State = EntityState.Detached;

            var existente = usuario.GoogleSub is not null
                ? await dbContext.Usuarios.FirstOrDefaultAsync(u => u.GoogleSub == usuario.GoogleSub, cancellationToken)
                : null;

            existente ??= await dbContext.Usuarios.FirstOrDefaultAsync(u => u.Email == usuario.Email, cancellationToken);

            return existente is not null
                ? Result<Usuario>.Success(existente)
                : Result<Usuario>.Failure(UsuarioErrors.NotFound);
        }
    }

    public Task<Result> UpdateAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        dbContext.Usuarios.Update(usuario);
        return Task.FromResult(Result.Success());
    }

    public async Task<Result<Usuario>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return usuario is not null
            ? Result<Usuario>.Success(usuario)
            : Result<Usuario>.Failure(UsuarioErrors.NotFound);
    }

    public async Task<Result<Usuario>> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken)
    {
        var usuario = await dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.GoogleSub == googleSub, cancellationToken);

        return usuario is not null
            ? Result<Usuario>.Success(usuario)
            : Result<Usuario>.Failure(UsuarioErrors.NotFound);
    }

    public async Task<Result<Usuario>> GetByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        var usuario = await dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        return usuario is not null
            ? Result<Usuario>.Success(usuario)
            : Result<Usuario>.Failure(UsuarioErrors.NotFound);
    }
}
