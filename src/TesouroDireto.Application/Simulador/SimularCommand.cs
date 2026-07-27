using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Simulador;

public sealed record SimularCommand(
    string Codigo,
    decimal ValorInvestido,
    DateOnly DataCompra,
    decimal TaxaContratada,
    decimal? ProjecaoAnual) : IRequest<Result<SimulacaoResultadoDto>>;
