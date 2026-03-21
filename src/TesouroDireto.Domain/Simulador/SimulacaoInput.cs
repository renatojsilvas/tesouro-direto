using TesouroDireto.Domain.Titulos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Domain.Simulador;

public sealed record SimulacaoInput(
    TipoTitulo TipoTitulo,
    decimal ValorInvestido,
    decimal TaxaContratada,
    DateOnly DataCompra,
    DateOnly DataVencimento,
    int DiasUteis,
    int DiasCorridos,
    decimal? ProjecaoAnual,
    IReadOnlyCollection<DateOnly> Feriados,
    IReadOnlyCollection<Tributo> TributosAtivos);
