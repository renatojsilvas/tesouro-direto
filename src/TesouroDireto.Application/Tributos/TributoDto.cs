namespace TesouroDireto.Application.Tributos;

public sealed record TributoDto(
    Guid Id,
    string Nome,
    string BaseCalculo,
    string TipoCalculo,
    IReadOnlyCollection<FaixaDto> Faixas,
    bool Ativo,
    int Ordem,
    bool Cumulativo);
