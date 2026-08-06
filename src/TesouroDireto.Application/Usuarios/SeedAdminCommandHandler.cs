using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Application.Usuarios;

public sealed class SeedAdminCommandHandler(
    IUsuarioWriteRepository usuarioWriteRepository,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SeedAdminCommandHandler> logger)
    : IRequestHandler<SeedAdminCommand, Result>
{
    public async Task<Result> Handle(SeedAdminCommand request, CancellationToken cancellationToken)
    {
        var adminEmail = configuration["ADMIN_EMAIL"];

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogInformation("ADMIN_EMAIL não configurado; seed de admin ignorado.");
            return Result.Success();
        }

        var emailResult = Email.Create(adminEmail);
        if (emailResult.IsFailure)
        {
            return Result.Failure(emailResult.Error);
        }

        var agora = timeProvider.GetUtcNow();

        var existente = await usuarioWriteRepository.GetByEmailAsync(emailResult.Value, cancellationToken);
        if (existente.IsSuccess)
        {
            var usuario = existente.Value;
            if (usuario.GarantirAdminAprovado(agora))
            {
                var update = await usuarioWriteRepository.UpdateAsync(usuario, cancellationToken);
                if (update.IsFailure)
                {
                    return Result.Failure(update.Error);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }

        var criacao = Usuario.Create(emailResult.Value, adminEmail, PapelUsuario.Admin, agora);
        if (criacao.IsFailure)
        {
            return Result.Failure(criacao.Error);
        }

        criacao.Value.GarantirAdminAprovado(agora);

        var add = await usuarioWriteRepository.AddAsync(criacao.Value, cancellationToken);
        if (add.IsFailure)
        {
            return Result.Failure(add.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
