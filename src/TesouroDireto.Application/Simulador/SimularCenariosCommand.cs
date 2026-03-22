using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Simulador;

public sealed record CenarioInput(string Nome, decimal ProjecaoAnual);

public sealed record SimularCenariosCommand(
    Guid TituloId,
    decimal ValorInvestido,
    DateOnly DataCompra,
    decimal TaxaContratada,
    IReadOnlyCollection<CenarioInput> Cenarios) : IRequest<Result<IReadOnlyCollection<CenarioResultadoDto>>>;

public sealed record CenarioResultadoDto(string Nome, SimulacaoResultadoDto Resultado);
