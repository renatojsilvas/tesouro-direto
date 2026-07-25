using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Contracts;

public sealed record CreateTributoRequest(
    string Nome,
    BaseCalculo BaseCalculo,
    TipoCalculo TipoCalculo,
    IReadOnlyCollection<FaixaDto> Faixas,
    int Ordem,
    bool Cumulativo);
