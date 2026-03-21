using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Simulador;

public static class SimuladorErrors
{
    public static readonly Error InvalidValorInvestido =
        new("Simulador.InvalidValorInvestido", "Valor investido must be greater than zero.");

    public static readonly Error ProjecaoRequired =
        new("Simulador.ProjecaoRequired", "Projecao anual is required for indexed titulos (Selic, IPCA, IGP-M).");
}
