using MediatR;
using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public sealed record CreateTributoCommand(
    string Nome,
    BaseCalculo BaseCalculo,
    TipoCalculo TipoCalculo,
    IReadOnlyCollection<FaixaDto> Faixas,
    int Ordem,
    bool Cumulativo) : IRequest<Result<Guid>>;
