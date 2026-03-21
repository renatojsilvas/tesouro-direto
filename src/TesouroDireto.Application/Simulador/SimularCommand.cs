using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Simulador;

public sealed record SimularCommand(
    Guid TituloId,
    decimal ValorInvestido,
    DateOnly DataCompra,
    decimal TaxaContratada,
    decimal? ProjecaoAnual) : IRequest<Result<SimulacaoResultadoDto>>;
