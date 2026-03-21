using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public sealed class UpdateTributoCommandHandler(
    ITributoReadRepository tributoReadRepository,
    ITributoWriteRepository tributoWriteRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTributoCommand, Result>
{
    public async Task<Result> Handle(UpdateTributoCommand request, CancellationToken cancellationToken)
    {
        var tributoResult = await tributoReadRepository.GetByIdAsync(request.Id, cancellationToken);
        if (tributoResult.IsFailure)
        {
            return Result.Failure(tributoResult.Error);
        }

        var tributo = tributoResult.Value;

        if (request.Ativo)
        {
            tributo.Ativar();
        }
        else
        {
            tributo.Desativar();
        }

        var faixas = new List<Faixa>();
        foreach (var faixaDto in request.Faixas)
        {
            var faixaResult = Faixa.Create(faixaDto.DiasMin, faixaDto.DiasMax, faixaDto.Dia, faixaDto.Aliquota);
            if (faixaResult.IsFailure)
            {
                return Result.Failure(faixaResult.Error);
            }

            faixas.Add(faixaResult.Value);
        }

        var updateFaixasResult = tributo.AtualizarFaixas(faixas);
        if (updateFaixasResult.IsFailure)
        {
            return updateFaixasResult;
        }

        var updateResult = await tributoWriteRepository.UpdateAsync(tributo, cancellationToken);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
