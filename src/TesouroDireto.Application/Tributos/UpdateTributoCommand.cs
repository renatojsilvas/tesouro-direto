using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed record UpdateTributoCommand(
    Guid Id,
    bool Ativo,
    IReadOnlyCollection<FaixaDto> Faixas) : IRequest<Result>;
