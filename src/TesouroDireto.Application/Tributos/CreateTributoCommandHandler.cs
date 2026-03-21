using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public sealed class CreateTributoCommandHandler(
    ITributoWriteRepository tributoWriteRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTributoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateTributoCommand request, CancellationToken cancellationToken)
    {
        var faixas = new List<Faixa>();
        foreach (var faixaDto in request.Faixas)
        {
            var faixaResult = Faixa.Create(faixaDto.DiasMin, faixaDto.DiasMax, faixaDto.Dia, faixaDto.Aliquota);
            if (faixaResult.IsFailure)
            {
                return faixaResult.Error;
            }

            faixas.Add(faixaResult.Value);
        }

        var tributoResult = Tributo.Create(
            request.Nome,
            request.BaseCalculo,
            request.TipoCalculo,
            faixas,
            request.Ordem,
            request.Cumulativo);

        if (tributoResult.IsFailure)
        {
            return tributoResult.Error;
        }

        var addResult = await tributoWriteRepository.AddAsync(tributoResult.Value, cancellationToken);
        if (addResult.IsFailure)
        {
            return addResult.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(tributoResult.Value.Id);
    }
}
