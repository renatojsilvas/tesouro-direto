using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed class SeedTributosCommandHandler(
    ITributoReadRepository tributoReadRepository,
    ITributoWriteRepository tributoWriteRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SeedTributosCommand, Result>
{
    public async Task<Result> Handle(SeedTributosCommand request, CancellationToken cancellationToken)
    {
        var existentes = await tributoReadRepository.GetAllAsync(cancellationToken);
        if (existentes.IsFailure)
        {
            return Result.Failure(existentes.Error);
        }

        if (existentes.Value.Count > 0)
        {
            return Result.Success(); // idempotente: já semeado
        }

        var padrao = TributosPadrao.Build();
        if (padrao.IsFailure)
        {
            return Result.Failure(padrao.Error);
        }

        foreach (var tributo in padrao.Value)
        {
            var add = await tributoWriteRepository.AddAsync(tributo, cancellationToken);
            if (add.IsFailure)
            {
                return Result.Failure(add.Error);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
